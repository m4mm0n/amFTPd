/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-12-02
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using amFTPd.Config.Ftpd;
using amFTPd.Config.Ident;
using amFTPd.Config.Vfs;
using amFTPd.Core.Ident;
using amFTPd.Core.Sections;
using amFTPd.Core.Vfs;
using amFTPd.Logging;
using amFTPd.Security;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
public sealed class FtpSession : IAsyncDisposable
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
    /// <summary>
    /// Represents the underlying TCP client used for control operations.
    /// </summary>
    /// <remarks>This property provides access to the <see cref="TcpClient"/> instance used for managing
    /// control connections.  It is read-only and cannot be modified after initialization.</remarks>
    public readonly TcpClient Control;
    /// <summary>
    /// Gets the user store that provides access to user-related operations and data.
    /// </summary>
    /// <remarks>This field is read-only and is intended to expose the underlying user store implementation. 
    /// Use this field to perform operations such as retrieving, creating, updating, or deleting user data.</remarks>
    public readonly IUserStore Users;
    /// <summary>
    /// Represents the file system used for FTP operations.
    /// </summary>
    /// <remarks>This field provides access to the underlying FTP file system, allowing operations such as
    /// file transfers, directory management, and other FTP-related tasks.</remarks>
    public readonly FtpFileSystem Fs;
    /// <summary>
    /// Gets the unique identifier for the current session.
    /// </summary>
    public int SessionId { get; }
    /// <summary>
    /// Retrieves a read-only collection of all active FTP sessions.
    /// </summary>
    /// <returns>A read-only collection of <see cref="FtpSession"/> objects representing the currently active sessions. The
    /// collection will be empty if no sessions are active.</returns>
    public static IReadOnlyCollection<FtpSession> GetActiveSessions()
        => _sessions.Values.ToList().AsReadOnly();
    /// <summary>
    /// Gets the FTP user account associated with the current session.
    /// </summary>
    public FtpUser? Account { get; private set; }
    /// <summary>
    /// Gets the timestamp of the most recent activity.
    /// </summary>
    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets a value indicating whether TLS (Transport Layer Security) is currently active.
    /// </summary>
    public bool TlsActive { get; private set; }
    /// <summary>
    /// Gets or sets the protection level or status associated with the object.
    /// </summary>
    public string Protection { get; set; }
    /// <summary>
    /// Gets a value indicating whether the user is currently logged in.
    /// </summary>
    public bool LoggedIn { get; private set; }
    /// <summary>
    /// Gets or sets the current working directory.
    /// </summary>
    public string Cwd { get; set; } = "/";
    /// <summary>
    /// Gets or sets the username of the user whose action is pending.
    /// </summary>
    public string? PendingUser { get; set; }
    /// <summary>
    /// Gets or sets the original name of the item before it was renamed.
    /// </summary>
    public string? RenameFrom { get; set; }
    /// <summary>
    /// Gets a value indicating whether a quit request has been made.
    /// </summary>
    public bool QuitRequested { get; private set; }
    /// <summary>
    /// Gets the username associated with the current user.
    /// </summary>
    public string? UserName { get; private set; }
    /// <summary>
    /// Gets or sets the offset value used to determine the starting point for processing or retrieval operations.
    /// </summary>
    public long? RestOffset { get; set; }
    /// <summary>
    /// Gets the identifier of the remote entity associated with this instance.
    /// </summary>
    public string? RemoteIdent { get; private set; }
    /// <summary>
    /// Gets the instance of the <see cref="IdentManager"/> used to manage identity-related operations.
    /// </summary>
    public IdentManager? IdentManager { get; }
    /// <summary>
    /// Gets the virtual file system manager responsible for managing file system operations.
    /// </summary>
    public VfsManager? VfsManager { get; }
    #endregion
    /// <summary>
    /// Initializes a new instance of the <see cref="FtpSession"/> class, representing an FTP session with the specified
    /// configuration, logging, and file system components.
    /// </summary>
    /// <remarks>This constructor initializes the session with the provided components and configuration. It
    /// sets up the control stream, initializes the Ident and VFS managers, and assigns a unique session ID.</remarks>
    /// <param name="control">The <see cref="TcpClient"/> used to manage the control connection for the FTP session.</param>
    /// <param name="log">The logger instance implementing <see cref="IFtpLogger"/> for logging session activity.</param>
    /// <param name="cfg">The <see cref="FtpConfig"/> object containing configuration settings for the FTP session.</param>
    /// <param name="users">The <see cref="IUserStore"/> instance used to manage user authentication and authorization.</param>
    /// <param name="fs">The <see cref="FtpFileSystem"/> instance representing the file system for the session.</param>
    /// <param name="defaultProt">The default protection level for the session, specified as a string.</param>
    /// <param name="tls">The <see cref="TlsConfig"/> object containing TLS configuration settings for secure communication.</param>
    /// <param name="identCfg">The <see cref="IdentConfig"/> object used to configure the Ident protocol for the session.</param>
    /// <param name="vfsCfg">The <see cref="VfsConfig"/> object used to configure the virtual file system for the session.</param>
    /// <param name="sectionResolver">The <see cref="SectionResolver"/> used to resolve sections for the VFS manager.</param>
    public FtpSession(
        TcpClient control,
        IFtpLogger log,
        FtpConfig cfg,
        IUserStore users,
        FtpFileSystem fs,
        string defaultProt,
        TlsConfig tls,
        IdentConfig identCfg,
        VfsConfig vfsCfg,
        SectionResolver sectionResolver)
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

        IdentManager = new IdentManager(identCfg);
        VfsManager = new VfsManager(
            vfsCfg.Mounts,
            vfsCfg.UserMounts,
            sectionResolver);
        SessionId = Interlocked.Increment(ref _nextSessionId);
        _sessions[SessionId] = this;
    }
    /// <summary>
    /// Marks the current operation as requesting to quit.
    /// </summary>
    /// <remarks>Sets the <see cref="QuitRequested"/> property to <see langword="true"/>.  This indicates that
    /// a quit operation has been requested.</remarks>
    public void MarkQuit() => QuitRequested = true;
    /// <summary>
    /// Clears the current value of the <see cref="RestOffset"/> property by setting it to <see langword="null"/>.
    /// </summary>
    /// <remarks>This method resets the <see cref="RestOffset"/> property to its default state.  Use this
    /// method when the offset is no longer needed or should be cleared.</remarks>
    public void ClearRestOffset() => RestOffset = null;
    /// <summary>
    /// Sets the current FTP user account and updates the session state.
    /// </summary>
    /// <remarks>This method updates the session to reflect the specified user account, including setting the
    /// username and marking the session as logged in.</remarks>
    /// <param name="account">The <see cref="FtpUser"/> object representing the user account to set. Cannot be <c>null</c>.</param>
    public void SetAccount(FtpUser account)
    {
        Account = account;
        UserName = account.UserName;
        LoggedIn = true; // ensure the session is considered logged in
    }
    /// <summary>
    /// Updates the <see cref="LastActivity"/> property to the current UTC date and time.
    /// </summary>
    /// <remarks>This method is typically used to record the most recent activity timestamp.  It sets the <see
    /// cref="LastActivity"/> property to <see cref="DateTimeOffset.UtcNow"/>.</remarks>
    public void Touch() => LastActivity = DateTimeOffset.UtcNow;
    /// <summary>
    /// Writes the specified string to the underlying stream asynchronously.
    /// </summary>
    /// <remarks>The string is encoded using ASCII encoding before being written to the stream. The method
    /// ensures that the data is flushed to the stream after writing.</remarks>
    /// <param name="s">The string to write to the stream. Cannot be <see langword="null"/>.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    public async Task WriteAsync(string s, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        await _ctrlStream.WriteAsync(bytes, 0, bytes.Length, ct);
        await _ctrlStream.FlushAsync(ct);
    }

    /// <summary>
    /// Upgrades the current connection to use TLS encryption.
    /// </summary>
    /// <remarks>This method initializes a secure connection using the provided TLS configuration. Once the
    /// upgrade is complete, the connection will use TLS for all subsequent communication.</remarks>
    /// <param name="tls">The TLS configuration settings used to authenticate the server.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task UpgradeToTlsAsync(TlsConfig tls, CancellationToken ct = default)
    {
        if (tls is null)
            throw new ArgumentNullException(nameof(tls));

        if (tls.Certificate is null)
            throw new InvalidOperationException("TLS configuration has no certificate.");

        // If already TLS, don’t re-wrap
        if (_ctrlStream is SslStream)
            return;

        var ssl = new SslStream(
            innerStream: _ctrlStream,
            leaveInnerStreamOpen: false // when TLS dies, so does the control socket
        );

        try
        {
            var options = tls.CreateServerOptions();
            if (options.ServerCertificate is null)
                throw new InvalidOperationException("TlsConfig returned options without a ServerCertificate.");

            await ssl.AuthenticateAsServerAsync(options, ct).ConfigureAwait(false);

            _ctrlStream = ssl;
            _log.Log(FtpLogLevel.Debug, "Control connection successfully upgraded to TLS.");
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Error, "TLS handshake failed on control connection.", ex);
            ssl.Dispose();
            throw;
        }
    }
    /// <summary>
    /// Opens a passive FTP data connection and binds it to an available port within the configured range.
    /// </summary>
    /// <remarks>This method attempts to bind the passive connection to a port within the range specified in
    /// the configuration. If no port is available, an <see cref="InvalidOperationException"/> is thrown.</remarks>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>The port number to which the passive connection is bound.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no passive port is available within the configured range.</exception>
    public async Task<int> OpenPassiveAsync(CancellationToken ct)
    {
        if (_data is not null)
            await _data.DisposeAsync();

        _data = new FtpDataConnection(_log, _tls, TlsActive, Protection);

        var local = (IPEndPoint)Control.Client.LocalEndPoint!;
        var bindAddress = local.Address;

        // Parse passive ports from the config string
        var portsToTry = new List<int>();

        var passiveRange = _cfg.PassivePorts;

        if (!string.IsNullOrWhiteSpace(passiveRange))
        {
            // First try "start-end" syntax
            var rangeParts = passiveRange.Split('-', 2);
            if (rangeParts.Length == 2 &&
                int.TryParse(rangeParts[0], out var startPort) &&
                int.TryParse(rangeParts[1], out var endPort) &&
                endPort >= startPort)
            {
                for (var port = startPort; port <= endPort; port++)
                    portsToTry.Add(port);
            }
            else
            {
                // Fallback: treat as comma-separated list "2121,2222,2323"
                foreach (var part in passiveRange.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out var port))
                        portsToTry.Add(port);
                }
            }
        }

        // If nothing was parsed, fall back to the main listening port (or pick whatever default you like)
        if (portsToTry.Count == 0)
        {
            portsToTry.Add((int)_cfg.Port);
        }

        var chosenPort = -1;
        foreach (var p in portsToTry)
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

        if (chosenPort < 0)
            throw new InvalidOperationException("No passive port available.");

        return chosenPort;
    }
    /// <summary>
    /// Establishes an active FTP data connection to the specified IP address and port.
    /// </summary>
    /// <remarks>If an existing data connection is open, it will be disposed before establishing the new
    /// connection.</remarks>
    /// <param name="ip">The IP address of the remote endpoint to connect to.</param>
    /// <param name="port">The port number of the remote endpoint to connect to.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns></returns>
    public async Task OpenActiveAsync(IPAddress ip, int port, CancellationToken ct)
    {
        if (_data is not null)
            await _data.DisposeAsync();

        _data = new FtpDataConnection(_log, _tls, TlsActive, Protection);
        await _data.SetActiveAsync(new IPEndPoint(ip, port), ct);
    }
    /// <summary>
    /// Executes the specified asynchronous action using the underlying data stream, if available.
    /// </summary>
    /// <remarks>This method invokes the provided <paramref name="action"/> with the data stream and ensures
    /// proper disposal of the stream after execution. If the underlying data stream is <c>null</c>, the method returns
    /// immediately without invoking the action.</remarks>
    /// <param name="action">A delegate that defines the asynchronous operation to perform on the data stream.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns></returns>
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
    /// <summary>
    /// Processes FTP commands asynchronously, handling client-server communication and managing the control connection.
    /// </summary>
    /// <remarks>This method reads incoming FTP commands from the control connection, processes them using the
    /// provided <paramref name="router"/>, and sends appropriate responses back to the client. It enforces an idle
    /// timeout based on the user's configuration or a default value of 30 minutes. The method terminates when the
    /// connection is closed, a quit command is received, or the operation is canceled.</remarks>
    /// <param name="router">The <see cref="FtpCommandRouter"/> responsible for routing and executing FTP commands.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests and terminate the operation if
    /// requested.</param>
    /// <returns></returns>
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
    /// <summary>
    /// Queries the remote client for its user identifier (IDENT) using the Identification Protocol (RFC 1413).
    /// </summary>
    /// <remarks>This method attempts to establish a connection to the remote client's IDENT service on port
    /// 113 and sends a query based on the local and remote port numbers. The response is parsed to extract the user
    /// identifier. If the IDENT service is unreachable, times out, or returns an invalid response, the method returns
    /// <see langword="null"/>.</remarks>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="string"/> containing the user identifier provided by the remote client, or <see langword="null"/>
    /// if the identifier cannot be retrieved or the IDENT service is unavailable.</returns>
    public async Task<string?> QueryIdentAsync(CancellationToken ct)
    {
        if (Control.Client.RemoteEndPoint is not IPEndPoint remote ||
            Control.Client.LocalEndPoint is not IPEndPoint local)
            return null;

        // RFC 1413: query is "server-port , client-port"
        // server-port = local.Port (our FTP server)
        // client-port = remote.Port (client's ephemeral port)
        var serverPort = local.Port;
        var clientPort = remote.Port;

        using var identClient = new TcpClient();
        try
        {
            // Small timeout so ident can't hang login forever.
            identClient.ReceiveTimeout = 5000;
            identClient.SendTimeout = 5000;

            await identClient.ConnectAsync(remote.Address, 113, ct);
        }
        catch
        {
            // Ident not reachable
            return null;
        }

        await using var stream = identClient?.GetStream();

        var query = Encoding.ASCII.GetBytes($"{serverPort} , {clientPort}\r\n");
        try
        {
            await stream?.WriteAsync(query, 0, query.Length, ct);
            await stream?.FlushAsync(ct);
        }
        catch
        {
            return null;
        }

        var buffer = new byte[512];
        int read;
        try
        {
            read = await stream?.ReadAsync(buffer, 0, buffer.Length, ct);
        }
        catch
        {
            return null;
        }

        if (read <= 0)
            return null;

        var resp = Encoding.ASCII.GetString(buffer, 0, read);

        // Expected format:
        // "server-port , client-port : USERID : <OS> : <username>\r\n"
        var parts = resp.Split(':');
        if (parts.Length < 3)
            return null;

        var userField = parts[^1].Trim();

        if (string.IsNullOrWhiteSpace(userField))
            return null;

        RemoteIdent = userField;
        return userField;
    }
    /// <summary>
    /// Asynchronously releases the resources used by the current instance.
    /// </summary>
    /// <remarks>This method performs cleanup operations such as removing the session, logging out the user,
    /// disposing of associated data, and closing the control. It ensures that all resources are released in an
    /// asynchronous manner. Exceptions during cleanup are caught and ignored.</remarks>
    /// <returns></returns>
    public async ValueTask DisposeAsync()
    {
        _sessions.TryRemove(SessionId, out _);

        if (Account is not null)
            try { Users.OnLogout(Account); } catch { /* ignore */ }

        if (_data is not null)
            await _data.DisposeAsync();

        try { Control.Close(); } catch { /* ignore */ }
        await Task.CompletedTask;
    }
}