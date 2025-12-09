/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IrcAnnouncer.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:51:58
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x720C7FC8
 *  
 *  Description:
 *      IRC announcer that subscribes to the EventBus and formats scene-style lines. For now, it only logs what it *would* se...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using amFTPd.Config.Irc;
using amFTPd.Core.Events;
using amFTPd.Logging;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace amFTPd.Core.Irc;

/// <summary>
/// IRC announcer that subscribes to the EventBus and formats scene-style lines.
/// For now, it only logs what it *would* send to IRC; you can wire real IRC I/O later.
/// </summary>
/// <summary>
/// IRC announcer that subscribes to the EventBus and announces events to IRC.
/// It handles connection, reconnection, PING/PONG and a simple send queue.
/// </summary>
public sealed class IrcAnnouncer : IAsyncDisposable
{
    private readonly IrcConfig _config;
    private readonly IFtpLogger _log;
    private readonly EventBus _bus;
    private readonly FishCodec? _fish;
    private readonly IIrcScriptHook? _scriptHook;
    private IrcScriptContext? _scriptContext;

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    private readonly ConcurrentQueue<string> _sendQueue = new();
    private readonly Lock _connLock = new();

    private TcpClient? _client;
    private StreamWriter? _writer;
    private StreamReader? _reader;

    private bool _isConnected;

    public IrcAnnouncer(IrcConfig config, IFtpLogger log, EventBus bus)
        : this(config, log, bus, fish: null, scriptHook: null)
    {
    }

    public IrcAnnouncer(IrcConfig config, IFtpLogger log, EventBus bus, FishCodec? fish)
        : this(config, log, bus, fish, scriptHook: null)
    {
    }

    /// <summary>
    /// Main constructor, allowing both FiSH and an IRC script hook.
    /// </summary>
    public IrcAnnouncer(
        IrcConfig config,
        IFtpLogger log,
        EventBus bus,
        FishCodec? fish,
        IIrcScriptHook? scriptHook)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _fish = fish;
        _scriptHook = scriptHook;

        _bus.Subscribe(HandleEvent);
    }

    /// <summary>
    /// Start the background IRC loop (connect, announce, reconnect).
    /// Safe to call only once.
    /// </summary>
    public void Start()
    {
        if (!_config.Enabled)
        {
            _log.Log(FtpLogLevel.Info, "[IRC] IRC announcer disabled by config (Enabled = false).");
            return;
        }

        if (_loop is not null)
            return;

        _log.Log(FtpLogLevel.Info, "[IRC] Starting IRC announcer loop...");
        _loop = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await EnsureConnectedAsync(ct).ConfigureAwait(false);
                if (!_isConnected)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    continue;
                }

                // Run main IO loop (read + send)
                await RunConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Error, $"[IRC] Fatal error in IRC loop: {ex}");
            }

            // If we get here, connection dropped; clean up and retry later.
            CleanupConnection();
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }

        CleanupConnection();
        _log.Log(FtpLogLevel.Info, "[IRC] IRC announcer loop stopped.");
    }

    private bool ValidateServerCertificate(
        object? sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) =>
        !_config.UseTls || _config.TlsAllowInvalidCerts || sslPolicyErrors == SslPolicyErrors.None;

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_isConnected)
            return;

        lock (_connLock)
            if (_isConnected)
                return;

        try
        {
            _log.Log(FtpLogLevel.Info, $"[IRC] Connecting to {_config.Server}:{_config.Port}...");

            var client = new TcpClient { NoDelay = true };
            var connectTask = client.ConnectAsync(_config.Server, _config.Port, ct);
            await using (ct.Register(() => client.Close())) await connectTask.ConfigureAwait(false);

            Stream stream = client.GetStream();

            // Optional TLS
            if (_config.UseTls)
            {
                var targetHost = _config.TlsServerName ?? _config.Server;
                var ssl = new SslStream(stream, false, ValidateServerCertificate);

                var options = new SslClientAuthenticationOptions
                {
                    TargetHost = targetHost,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                };

                await ssl.AuthenticateAsClientAsync(options, ct).ConfigureAwait(false);
                stream = ssl;

                _log.Log(FtpLogLevel.Info, "[IRC] TLS handshake completed.");
            }

            var writer = new StreamWriter(stream, Encoding.UTF8)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };
            var reader = new StreamReader(stream, Encoding.UTF8);

            // Build script context now that we have a live writer
            var scriptContext = new IrcScriptContext(
                _config,
                _log,
                raw => SendRawAsync(raw));

            var useScript = _config.ScriptEnabled && _scriptHook is not null;
            var scriptHandledRegister = false;

            if (useScript)
            {
                try
                {
                    scriptHandledRegister = await _scriptHook!.OnRegisterAsync(scriptContext).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Log(FtpLogLevel.Warn, $"[IRC] Script OnRegisterAsync failed: {ex.Message}", ex);
                }
            }

            if (!scriptHandledRegister)
            {
                // Optional PASS
                if (!string.IsNullOrEmpty(_config.ServerPassword))
                    await writer.WriteLineAsync($"PASS {_config.ServerPassword}").ConfigureAwait(false);

                // Default registration
                await writer.WriteLineAsync($"NICK {_config.Nick}").ConfigureAwait(false);
                await writer.WriteLineAsync($"USER {_config.User} 0 * :{_config.RealName}").ConfigureAwait(false);

                // Default channel joins
                foreach (var chan in _config.GetChannelList()) await writer.WriteLineAsync($"JOIN {chan}").ConfigureAwait(false);
            }

            lock (_connLock)
            {
                _client = client;
                _writer = writer;
                _reader = reader;
                _isConnected = true;
                _scriptContext = scriptContext;
            }

            _log.Log(FtpLogLevel.Info, "[IRC] Connected and joined channels (or script-registered).");

            // Notify script that we are fully connected
            if (useScript)
            {
                try
                {
                    await _scriptHook!.OnConnectedAsync(scriptContext).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Log(FtpLogLevel.Warn, $"[IRC] Script OnConnectedAsync failed: {ex.Message}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Error, $"[IRC] Failed to connect to IRC: {ex.Message}", ex);
            CleanupConnection();
        }
    }

    private async Task RunConnectionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isConnected)
        {
            // Read line if available (without blocking forever)
            string? line = null;
            try
            {
                if (_reader is not null && _client is { Connected: true })
                {
                    // Use ReadLineAsync, but we still rely on cancellation to break out
                    var readTask = _reader.ReadLineAsync();
                    var completed = await Task.WhenAny(readTask, Task.Delay(100, ct)).ConfigureAwait(false);
                    if (completed == readTask)
                    {
                        line = readTask.Result;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn, $"[IRC] Read error: {ex.Message}", ex);
                break;
            }

            if (line is not null)
            {
                HandleIncomingLine(line);
            }

            // Flush send queue
            await FlushSendQueueAsync(ct).ConfigureAwait(false);
        }

        CleanupConnection();
    }

    private void HandleIncomingLine(string line)
    {
        if (line.StartsWith("PING", StringComparison.OrdinalIgnoreCase))
        {
            var token = line.Length > 4 ? line[4..].TrimStart(':', ' ') : "";
            _ = SendRawAsync($"PONG :{token}");
        }

        var ctx = _scriptContext;
        var hook = _scriptHook;

        if (_config.ScriptEnabled && ctx is not null && hook is not null)
        {
            // Fire and forget – script errors are logged but don't kill the loop.
            _ = Task.Run(async () =>
            {
                try
                {
                    await hook.OnIncomingLineAsync(ctx, line).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ctx.Log.Log(FtpLogLevel.Warn, $"[IRC] Script OnIncomingLineAsync failed: {ex.Message}", ex);
                }
            });
        }

        // optional debug log...
        // _log.Log(FtpLogLevel.Debug, $"[IRC] << {line}");
    }

    private async Task FlushSendQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isConnected && _sendQueue.TryDequeue(out var msg))
        {
            foreach (var chan in _config.GetChannelList())
            {
                var outMsg = msg;

                if (_config.FishEnabled && _fish is not null && _fish.HasKeyForTarget(chan))
                {
                    try
                    {
                        outMsg = _fish.EncryptMessage(chan, msg);
                    }
                    catch (Exception ex)
                    {
                        _log.Log(FtpLogLevel.Warn,
                            $"[IRC] FiSH encryption failed for channel {chan}: {ex.Message}", ex);
                        // fall back to plaintext
                        outMsg = msg;
                    }
                }

                await SendRawAsync($"PRIVMSG {chan} :{outMsg}").ConfigureAwait(false);
            }
        }
    }

    private Task SendRawAsync(string line)
    {
        StreamWriter? writer;
        lock (_connLock)
        {
            writer = _writer;
        }

        if (writer is null)
            return Task.CompletedTask;

        try
        {
            // _log.Log(FtpLogLevel.Debug, $"[IRC] >> {line}");
            return writer.WriteLineAsync(line);
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Warn, $"[IRC] Failed to send line: {ex.Message}", ex);
            return Task.CompletedTask;
        }
    }

    private void HandleEvent(FtpEvent ev)
    {
        if (!_config.Enabled)
            return;

        var line = FormatLine(ev);
        if (string.IsNullOrEmpty(line))
            return;

        _sendQueue.Enqueue(line);
    }

    private static string? FormatLine(FtpEvent ev)
    {
        // Keep it small for now; can be extended later.
        return ev.Type switch
        {
            FtpEventType.Pre => FormatPre(ev),
            FtpEventType.Nuke => FormatNuke(ev),
            FtpEventType.RaceComplete => FormatRaceComplete(ev),
            FtpEventType.Upload => FormatUpload(ev),
            FtpEventType.ZipscriptStatus => FormatZipscript(ev),
            _ => null
        };
    }

    private static string FormatPre(FtpEvent ev)
    {
        var rel = ev.ReleaseName ?? ev.VirtualPath ?? "(unknown)";
        var sec = ev.Section ?? "(no-sec)";
        var user = ev.User ?? "(unknown)";
        return $"*** PRE: {rel} in {sec} by {user}";
    }

    private static string FormatNuke(FtpEvent ev)
    {
        var rel = ev.ReleaseName ?? ev.VirtualPath ?? "(unknown)";
        var reason = ev.Reason ?? "no reason";
        var user = ev.User ?? "(unknown)";

        var multText = "";
        if (!string.IsNullOrEmpty(ev.Extra))
        {
            // crude parse: "mult=3" from Extra
            const string key = "mult=";
            var idx = ev.Extra.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = ev.Extra[(idx + key.Length)..].Trim();
                var sep = rest.IndexOfAny(new[] { ' ', ';', ',' });
                var mult = sep >= 0 ? rest[..sep] : rest;
                multText = $" x{mult}";
            }
        }

        return $"*** NUKE: {rel}{multText} ({reason}) by {user}";
    }

    private static string FormatRaceComplete(FtpEvent ev)
    {
        var rel = ev.ReleaseName ?? ev.VirtualPath ?? "(unknown)";
        var mb = ev.Bytes.HasValue
            ? (ev.Bytes.Value / (1024.0 * 1024.0)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            : "?";
        return $"*** RACE COMPLETE: {rel} ({mb} MB)";
    }

    private static string FormatUpload(FtpEvent ev)
    {
        var user = ev.User ?? "(unknown)";
        var sec = ev.Section ?? "(no-sec)";
        var rel = ev.ReleaseName ?? ev.VirtualPath ?? "(unknown)";
        var mb = ev.Bytes.HasValue
            ? (ev.Bytes.Value / (1024.0 * 1024.0)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            : "?";

        return $"*** UPLOAD: {user} -> {sec} {rel} ({mb} MB)";
    }

    private static string FormatZipscript(FtpEvent ev)
    {
        var rel = ev.ReleaseName ?? ev.VirtualPath ?? "(unknown)";
        var status = ev.Reason ?? "UNKNOWN"; // you can put COMPLETE/INCOMPLETE in Reason
        return $"*** SFV: {rel} {status}";
    }

    private void CleanupConnection()
    {
        lock (_connLock)
        {
            if (_client is not null)
            {
                try { _client.Close(); } catch { /* ignore */ }
            }

            _client = null;
            _writer = null;
            _reader = null;
            _isConnected = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }
        _cts.Dispose();
        CleanupConnection();
    }
}