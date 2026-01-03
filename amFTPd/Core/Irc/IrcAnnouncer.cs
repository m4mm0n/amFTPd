/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IrcAnnouncer.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:51:58
 *  Last Modified:  2025-12-14 18:06:52
 *  CRC32:          0xDE57EAD3
 *  
 *  Description:
 *      IRC announcer that subscribes to the EventBus and announces events to IRC. It handles connection, reconnection, PING/...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Config.Irc;
using amFTPd.Core.Events;
using amFTPd.Core.Irc.FiSH;
using amFTPd.Logging;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace amFTPd.Core.Irc;

enum IrcState
{
    Disconnected,
    Connecting,
    Registering,
    Registered
}

/// <summary>
/// IRC announcer that subscribes to the EventBus and announces events to IRC.
/// It handles connection, reconnection, PING/PONG and a simple send queue.
/// </summary>
public sealed class IrcAnnouncer : IAsyncDisposable
{
    private readonly IFtpLogger _log;
    private readonly EventBus _bus;
    private IrcConfig _config;

    private readonly FishKeyStore _keys = new();
    private readonly Dh1080Manager _dh1080;

    private string _currentNick = string.Empty;
    private int _maxNickLength = 30; // safe default
    private int _nickAttempts;

#if DEBUG
    private readonly IrcWireLogger _wire;
#endif

    private TcpClient? _client;
    private StreamReader? _reader;
    private Stream? _netStream;

    private Task? _loop;
    private readonly CancellationTokenSource _cts = new();

    private static readonly Encoding IrcEncoding = Encoding.GetEncoding(28591);
    private IrcState _state = IrcState.Disconnected;
    private IIrcScriptHook? _scriptHook;

    public IrcAnnouncer(
        IrcConfig config,
        IFtpLogger log,
        EventBus bus,
        IIrcScriptHook? scriptHook)
    {
        _config = config;
        _log = log;
        _bus = bus;
        _scriptHook = scriptHook;   
        _dh1080 = new Dh1080Manager(log);

#if DEBUG
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        _wire = new IrcWireLogger(Path.Combine(logDir, "irc-announcer.log"));
#endif

        // preload static channel keys
        foreach (var kv in _config.FishKeys)
            _keys.AddEcb(kv.Key, kv.Value);

        _bus.Subscribe(OnEvent);
    }

    // ------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------

    public void Start()
    {
        if (!_config.Enabled || _loop != null)
            return;

        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task ReloadAsync(IrcConfig? newConfig)
    {
        await DisposeAsync();

        if (newConfig is { Enabled: true })
        {
            _config = newConfig;
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    // ------------------------------------------------------------
    // Main loop
    // ------------------------------------------------------------

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRegisterAsync(ct);
                await SessionLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn, $"[IRC] Disconnected: {ex.Message}", ex);
            }

            Cleanup();
            _state = IrcState.Disconnected;
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    // ------------------------------------------------------------
    // Connection
    // ------------------------------------------------------------

    private async Task ConnectAndRegisterAsync(CancellationToken ct)
    {
        _state = IrcState.Connecting;

        _client = new TcpClient();
        await _client.ConnectAsync(_config.Server, _config.Port, ct);

        var ssl = new SslStream(
            _client.GetStream(),
            false,
            (_, _, _, _) => _config.TlsAllowInvalidCerts);

        await ssl.AuthenticateAsClientAsync(_config.TlsServerName ?? _config.Server);

        _netStream = ssl;
        _reader = new StreamReader(ssl, IrcEncoding, false, 1024, leaveOpen: true);

        _state = IrcState.Registering;

        await SendRawAsync("CAP LS 302");

        if (!string.IsNullOrEmpty(_config.ServerPassword))
            await SendRawAsync($"PASS {_config.ServerPassword}");

        await SendRawAsync($"NICK {_config.Nick}");
        await SendRawAsync($"USER {_config.User} 0 * :{_config.RealName}");
    }

    // ------------------------------------------------------------
    // IRC session
    // ------------------------------------------------------------

    private async Task SessionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _client?.Connected == true)
        {
            var line = await _reader!.ReadLineAsync(ct);
            if (line == null)
                throw new IOException("IRC connection closed");

#if DEBUG
            _wire.Receive(line);
#endif

            if (line.StartsWith("PING "))
            {
                await SendRawAsync("PONG " + line[5..]);
                continue;
            }

            HandleIncomingLine(line);
        }
    }

    // --- SNIP header unchanged ---

    private void HandleIncomingLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        string? prefix = null;
        string command;
        List<string> args = new();

        var idx = 0;

        // Prefix
        if (line[idx] == ':')
        {
            var end = line.IndexOf(' ');
            if (end == -1)
                return;

            prefix = line.Substring(1, end - 1);
            idx = end + 1;
        }

        // Command
        var cmdEnd = line.IndexOf(' ', idx);
        if (cmdEnd == -1)
        {
            command = line[idx..];
        }
        else
        {
            command = line[idx..cmdEnd];
            idx = cmdEnd + 1;

            // Params
            while (idx < line.Length)
            {
                if (line[idx] == ':')
                {
                    args.Add(line[(idx + 1)..]);
                    break;
                }

                var next = line.IndexOf(' ', idx);
                if (next == -1)
                {
                    args.Add(line[idx..]);
                    break;
                }

                args.Add(line[idx..next]);
                idx = next + 1;
            }
        }

        switch (command)
        {
            // ─────────────────────────────────────────────
            // Registration / connection lifecycle
            // ─────────────────────────────────────────────

            case "001": // RPL_WELCOME
                        // args[0] is always OUR current nick
                _currentNick = args[0];

                _log.Log(FtpLogLevel.Info,
                    $"[IRC] Registered as {_currentNick}");

                _ = OnRegisteredAsync();
                return;

            case "005": // RPL_ISUPPORT
                foreach (var arg in args)
                {
                    if (arg.StartsWith("NICKLEN=", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(arg.AsSpan(8), out var len))
                    {
                        _maxNickLength = len;
                        _log.Log(FtpLogLevel.Debug,
                            $"[IRC] Server nick length limit: {_maxNickLength}");
                    }

                    // Future-proofing (cheap to keep)
                    // CHANTYPES=#&
                    // PREFIX=(ov)@+
                    // MODES=4
                    // CASEMAPPING=rfc1459
                }
                return;

            // ─────────────────────────────────────────────
            // Keepalive
            // ─────────────────────────────────────────────

            case "PING":
                if (args.Count > 0)
                    _ = SendRawAsync("PONG :" + args[0]);
                return;

            // ─────────────────────────────────────────────
            // Nick handling / collisions
            // ─────────────────────────────────────────────

            case "432": // ERR_ERRONEUSNICKNAME
            case "433": // ERR_NICKNAMEINUSE
            case "437": // ERR_UNAVAILRESOURCE
                _log.Log(FtpLogLevel.Warn,
                    $"[IRC] Nick collision or invalid nick ({command}), choosing new nick");

                if (++_nickAttempts > 5)
                {
                    _log.Log(FtpLogLevel.Error,
                        "[IRC] Too many nick collisions, giving up");
                    return;
                }

                _ = HandleNickCollisionAsync();
                return;
            
            case "MODE":
            case "KICK":
            case "INVITE":
                break;

            case "NICK":
            {
                // Prefix MUST exist for NICK
                if (prefix == null || args.Count < 1)
                    return;

                var oldNick = prefix.Split('!')[0];
                var newNick = args[0];

                _log.Log(FtpLogLevel.Info,
                    $"[IRC] Nick change: {oldNick} → {newNick}");

                // If it's us, update identity
                if (oldNick.Equals(_currentNick, StringComparison.OrdinalIgnoreCase))
                {
                    _currentNick = newNick;
                    _nickAttempts = 0;

                    _log.Log(FtpLogLevel.Info,
                        $"[IRC] Our nick is now {_currentNick}");
                }

                // 🔐 Rebind FiSH keys
                _keys.Rebind(oldNick, newNick);

                // 🔁 Rebind DH1080 session (if mid-handshake)
                _dh1080.Rebind(oldNick, newNick);
                return;
            }

            // ─────────────────────────────────────────────
            // Channel / presence numerics (useful later)
            // ─────────────────────────────────────────────

            case "353": // RPL_NAMREPLY
                        // args: <me> <symbol> <channel> :nick nick nick
                        // Useful later for encrypted channel announce routing
                return;

            case "366": // RPL_ENDOFNAMES
                        // End of NAMES list
                return;

            case "332": // RPL_TOPIC
                        // Channel topic
                return;

            case "333": // RPL_TOPICWHOTIME
                        // Topic metadata
                return;

            // ─────────────────────────────────────────────
            // Errors worth knowing about
            // ─────────────────────────────────────────────

            case "401": // ERR_NOSUCHNICK
            case "403": // ERR_NOSUCHCHANNEL
            case "404": // ERR_CANNOTSENDTOCHAN
                _log.Log(FtpLogLevel.Debug,
                    $"[IRC] Target error ({command}): {string.Join(' ', args)}");
                return;

            // ─────────────────────────────────────────────
            // User / server messages
            // ─────────────────────────────────────────────

            case "NOTICE":
            case "PRIVMSG":
                if (args.Count >= 2 && prefix != null)
                {
                    var from = prefix.Split('!')[0];
                    var target = args[0];
                    var msg = args[1];
                    var isNotice = command == "NOTICE";

                    HandleIncomingMessage(from, target, msg, isNotice);
                }
                return;

            // ─────────────────────────────────────────────
            // Default: ignore silently
            // ─────────────────────────────────────────────

            default:
                return;
        }
    }

    private void HandleIncomingMessage(
        string from,
        string target,
        string msg,
        bool isNotice)
    {
        if (isNotice)
        {
            // Normalize IRC payload (strip CTCP / BOM / control chars)
            msg = msg.TrimStart(
                '\u0001', // CTCP
                '\uFEFF', // BOM / zero-width
                '\0', '\r', '\n', '\t', ' '
            );

            if (msg.StartsWith("DH1080_", StringComparison.Ordinal))
            {
                try
                {
                    if (!target.Equals(_currentNick, StringComparison.OrdinalIgnoreCase))
                        return;

                    if (!_dh1080.TryGet(from, out var session)) session = _dh1080.Start(from);

                    if (msg.StartsWith("DH1080_INIT", StringComparison.Ordinal))
                    {
                        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2)
                        {
                            _log.Log(FtpLogLevel.Warn, "[IRC] Malformed DH1080_INIT");
                            return;
                        }

                        var keyPart = parts[1];

                        var reply = session.HandleInit(keyPart);
                        if (string.IsNullOrEmpty(reply))
                        {
                            _log.Log(FtpLogLevel.Warn,
                                "[IRC] DH1080 HandleInit returned empty reply");
                            return;
                        }

                        _ = SendNoticeAsync(from, reply);
                        _keys.MarkPending(from);
                        return;
                    }

                    if (msg.StartsWith("DH1080_FINISH", StringComparison.Ordinal))
                    {
                        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2)
                            return;

                        session.HandleFinish(parts[1]);

                        var key = session.DeriveFishKey();
                        _keys.UpgradeToCbc(from, key);
                        _dh1080.Remove(from);

                        _log.Log(FtpLogLevel.Info,
                            $"[IRC] DH1080 CBC key established with {from}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _log.Log(FtpLogLevel.Error, "[IRC] DH1080 handler crashed it seems!", ex);
                }
            }
        }

        // --------------------
        // FiSH decrypt (NOTICE or PRIVMSG)
        // --------------------
        if (msg.StartsWith("+OK ") && _keys.TryGet(from, out var entry))
        {
            try
            {
                var fish = new Fish(entry.Key, entry.Mode);
                var plain = fish.Decrypt(msg);

                _log.Log(FtpLogLevel.Debug,
                    $"[IRC] <{from}> {plain}");
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn,
                    $"[IRC] FiSH decrypt failed from {from}", ex);
            }
        }
    }

    private async Task SendNoticeAsync(string target, string message) => await SendRawAsync($"NOTICE {target} :{message}");

    // ------------------------------------------------------------
    // Registration
    // ------------------------------------------------------------

    private async Task OnRegisteredAsync()
    {
        if (_state == IrcState.Registered)
            return;

        _state = IrcState.Registered;

        foreach (var chan in _config.GetChannelList())
        {
            if (_config.FishKeys.TryGetValue(chan, out var key))
                await SendRawAsync($"JOIN {chan} {key}");
            else
                await SendRawAsync($"JOIN {chan}");
        }

        _log.Log(FtpLogLevel.Info, "[IRC] Connected and registered.");
    }

    private async Task HandleNickCollisionAsync()
    {
        var suffix = "_" + Random.Shared.Next(10, 99);
        var maxBaseLen = Math.Max(1, _maxNickLength - suffix.Length);

        var baseNick = _config.Nick.Length > maxBaseLen
            ? _config.Nick[..maxBaseLen]
            : _config.Nick;

        var newNick = baseNick + suffix;

        _log.Log(FtpLogLevel.Info,
            $"[IRC] Trying alternate nick: {newNick}");

        await SendRawAsync($"NICK {newNick}");
    }

    // ------------------------------------------------------------
    // Sending
    // ------------------------------------------------------------

    private async Task SendPrivMsgAsync(string target, string message)
    {
        if (_keys.TryGet(target, out var key))
        {
            var fish = new Fish(key.Key, key.Mode);
            message = fish.Encrypt(message);
        }

        await SendRawAsync($"PRIVMSG {target} :{message}");
    }

    private async Task SendRawAsync(string line)
    {
#if DEBUG
        _wire.Send(line);
#endif
        _log.Log(FtpLogLevel.Debug, $"[IRC] RAW >> {line}");

        var data = IrcEncoding.GetBytes(line + "\r\n");
        await _netStream!.WriteAsync(data, 0, data.Length);
        await _netStream.FlushAsync();
    }

    // ------------------------------------------------------------
    // Event handling
    // ------------------------------------------------------------

    private void OnEvent(FtpEvent ev)
    {
        if (_state != IrcState.Registered)
            return;

        switch (ev.Type)
        {
            case FtpEventType.Pre:
                if (ev.Section != null && ev.ReleaseName != null)
                    _ = SendPreAsync(ev);
                break;

            case FtpEventType.Nuke:
                _ = SendNukeAsync(ev);
                break;

            case FtpEventType.Unnuke:
                _ = SendUnnukeAsync(ev);
                break;

            case FtpEventType.Delete:
                _ = SendDeleteAsync(ev);
                break;
        }
    }

    private async Task SendPreAsync(FtpEvent ev)
    {
        foreach (var chan in _config.GetChannelList()) await SendPrivMsgAsync(chan, $"PRE {ev.Section} {ev.ReleaseName}");
    }
    private async Task SendNukeAsync(FtpEvent ev)
    {
        foreach (var chan in _config.GetChannelList())
        {
            var msg =
                $"NUKE {ev.Section} {ev.ReleaseName} " +
                $"{ev.Reason ?? "no-reason"}";

            await SendPrivMsgAsync(chan, msg);
        }
    }
    private async Task SendUnnukeAsync(FtpEvent ev)
    {
        foreach (var chan in _config.GetChannelList())
        {
            var msg =
                $"UNNUKE {ev.Section} {ev.ReleaseName}";

            await SendPrivMsgAsync(chan, msg);
        }
    }
    private async Task SendDeleteAsync(FtpEvent ev)
    {
        foreach (var chan in _config.GetChannelList())
        {
            var msg =
                $"DEL {ev.Section} {ev.ReleaseName}";

            await SendPrivMsgAsync(chan, msg);
        }
    }

    // ------------------------------------------------------------
    // Cleanup
    // ------------------------------------------------------------

    private void Cleanup()
    {
        try { _reader?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }

        _reader = null;
        _client = null;
        _netStream = null;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop != null)
            await _loop;

        Cleanup();
    }
}