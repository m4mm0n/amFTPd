using amFTPd.Config.Ftpd;
using amFTPd.Logging;
using amFTPd.Security;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace amFTPd.Core;

/// <summary>
/// Represents an FTP session that manages the control and data connections, user authentication,  and command handling
/// for an FTP server.
/// </summary>
/// <remarks>This class encapsulates the state and behavior of an individual FTP session, including user 
/// authentication, file system access, and data transfer. It supports both active and passive  data connections and can
/// upgrade the control connection to use TLS for secure communication.  Instances of this class are created for each
/// client connection and are responsible for  maintaining session-specific state, such as the current working
/// directory, user account,  and transfer settings. The session also tracks activity timestamps for idle timeout
/// management.</remarks>
internal sealed class FtpSession : IAsyncDisposable
{
    #region Private Fields
    private readonly NetworkStream _baseStream;
    private Stream _ctrlStream;

    private readonly IFtpLogger _log;
    private readonly FtpConfig _cfg;
    private readonly TlsConfig _tls;

    private FtpDataConnection? _data;

    private static readonly ConcurrentDictionary<int, FtpSession> _sessions = new();
    private static int _nextSessionId;
    #endregion
    #region Public Properties and Methods
    public readonly TcpClient Control;
    public readonly IUserStore Users;
    public readonly FtpFileSystem Fs;
    public int SessionId { get; }
    public static IReadOnlyCollection<FtpSession> GetActiveSessions()
        => _sessions.Values.ToList().AsReadOnly();
    public FtpUser? Account { get; private set; }
    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
    public void Touch() => LastActivity = DateTimeOffset.UtcNow;
    public bool TlsActive { get; private set; }
    public string Protection { get; set; }
    public bool LoggedIn { get; private set; }
    public string Cwd { get; set; } = "/";
    public string HomeDir { get; internal set; } = "/";
    public string? PendingUser { get; set; }
    public string? RenameFrom { get; set; }
    public bool QuitRequested { get; private set; }

    public string? UserName { get; private set; }
    public long? RestOffset { get; set; }
    #endregion
    public void SetAccount(FtpUser account)
    {
        Account = account;
        UserName = account.UserName;
        LoggedIn = true; // ensure the session is considered logged in
    }

    public FtpSession(
        TcpClient control,
        IFtpLogger log,
        FtpConfig cfg,
        IUserStore users,
        FtpFileSystem fs,
        string defaultProt,
        TlsConfig tls)
    {
        Control = control;
        _log = log;
        _cfg = cfg;
        Users = users;
        Fs = fs;
        _baseStream = control.GetStream();
        _ctrlStream = _baseStream;
        Protection = defaultProt;
        TlsActive = false;
        _tls = tls;

        SessionId = Interlocked.Increment(ref _nextSessionId);
        _sessions[SessionId] = this;
    }

    public void MarkQuit() => QuitRequested = true;

    public void Login(FtpUser account)
    {
        Account = account;
        UserName = account.UserName;
        LoggedIn = true;
        Cwd = FtpPath.Normalize("/", account.HomeDir);
        RestOffset = null;
        Touch();
    }

    public void ClearRestOffset() => RestOffset = null;

    public async Task WriteAsync(string s, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        await _ctrlStream.WriteAsync(bytes, 0, bytes.Length, ct);
        await _ctrlStream.FlushAsync(ct);
    }

    public async Task UpgradeToTlsAsync(TlsConfig tls)
    {
        var ssl = new SslStream(_baseStream, leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsServerAsync(tls.CreateServerOptions());
        _ctrlStream = ssl;
        TlsActive = true;
    }

    public async Task<int> OpenPassiveAsync(CancellationToken ct)
    {
        if (_data is not null)
            await _data.DisposeAsync();

        _data = new FtpDataConnection(_log, _tls, TlsActive, Protection);

        var local = (IPEndPoint)Control.Client.LocalEndPoint!;
        var bindAddress = local.Address;

        var chosenPort = -1;
        for (var p = _cfg.PassivePorts.Start; p <= _cfg.PassivePorts.End; p++)
        {
            try
            {
                chosenPort = await _data.StartPassiveAsync(bindAddress, p, ct);
                break;
            }
            catch
            {
                // try next
            }
        }

        return chosenPort < 0 ? throw new InvalidOperationException("No passive port available.") : chosenPort;
    }

    public async Task OpenActiveAsync(IPAddress ip, int port, CancellationToken ct)
    {
        if (_data is not null)
            await _data.DisposeAsync();

        _data = new FtpDataConnection(_log, _tls, TlsActive, Protection);
        await _data.SetActiveAsync(new IPEndPoint(ip, port), ct);
    }

    public async Task WithDataAsync(Func<Stream, Task> action, CancellationToken ct)
    {
        if (_data is null)
            return;

        try
        {
            await _data.SendAsync(action, ct);
        }
        finally
        {
            await _data.DisposeAsync();
            _data = null;
        }
    }

    public async Task RunAsync(FtpCommandRouter router, CancellationToken ct)
    {
        await WriteAsync(FtpResponses.Banner(_cfg.WelcomeMessage), ct);

        var buffer = new byte[8192];
        var sb = new StringBuilder();

        while (!ct.IsCancellationRequested && Control.Connected && !QuitRequested)
        {
            // Idle timeout (per user, else default 30 min)
            var idleTimeout = Account?.IdleTimeout ?? TimeSpan.FromMinutes(30);
            if (idleTimeout > TimeSpan.Zero &&
                DateTimeOffset.UtcNow - LastActivity > idleTimeout)
            {
                await WriteAsync("421 Idle timeout, closing control connection.\r\n", ct);
                break;
            }

            int n;
            try
            {
                n = await _ctrlStream.ReadAsync(buffer, 0, buffer.Length, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Error, "Control channel read error", ex);
                break;
            }

            if (n <= 0)
                break;

            sb.Append(Encoding.ASCII.GetString(buffer, 0, n));

            while (true)
            {
                var text = sb.ToString();
                var idx = text.IndexOf("\r\n", StringComparison.Ordinal);
                if (idx < 0)
                    break;

                var line = text[..idx];
                sb.Remove(0, idx + 2);

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    await router.HandleAsync(line, ct);
                }
                catch (Exception ex)
                {
                    _log.Log(FtpLogLevel.Error, $"Command error: {line}", ex);
                    await WriteAsync("451 Requested action aborted. Local error.\r\n", ct);
                }

                if (QuitRequested)
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _sessions.TryRemove(SessionId, out _);

        if (Account is not null)
        {
            try { Users.OnLogout(Account); } catch { /* ignore */ }
        }

        if (_data is not null)
            await _data.DisposeAsync();

        try { Control.Close(); } catch { /* ignore */ }
        await Task.CompletedTask;
    }
}