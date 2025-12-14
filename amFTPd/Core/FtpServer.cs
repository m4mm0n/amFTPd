/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpServer.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 21:17:28
 *  CRC32:          0xACD6F4CD
 *  
 *  Description:
 *      Represents an FTP(S) server that can handle client connections, manage user authentication,  and facilitate file tran...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Config.Ident;
using amFTPd.Config.Scripting;
using amFTPd.Config.Vfs;
using amFTPd.Core.Sections;
using amFTPd.Core.Stats;
using amFTPd.Db;
using amFTPd.Logging;
using amFTPd.Scripting;
using amFTPd.Security;
using amFTPd.Security.BanList;
using amFTPd.Security.HammerGuard;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace amFTPd.Core;

/// <summary>
/// Represents an FTP(S) server that can handle client connections, manage user authentication,  and facilitate file
/// transfers over the FTP protocol with optional TLS encryption.
/// </summary>
/// <remarks>The <see cref="FtpServer"/> class provides methods to start and stop the server,  allowing it to
/// listen for incoming client connections and handle FTP commands.  It supports secure communication using TLS,
/// configurable logging, and user authentication  through an external user store. The server operates asynchronously to
/// handle multiple  client sessions concurrently.</remarks>
public sealed class FtpServer
{
    #region Private Fields
    private FtpConfig _cfg;
    private IUserStore _users;
    private TlsConfig _tls;
    private readonly IFtpLogger _log;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private SectionManager _sections;

    private IdentConfig _identCfg;
    private VfsConfig _vfsCfg;
    private SectionResolver _sectionResolver;

    private HammerGuard _hammerGuard;
    private readonly BanList _banList;

    private readonly Lock _connectionLock = new();
    private readonly ConcurrentDictionary<IPAddress, int> _connectionsPerIp = new();
    private int _currentConnections;

    private AmFtpdRuntimeConfig _runtime;
    private readonly string _configPath;
    private readonly Lock _configLock = new();

    private readonly SessionLogWriter? _sessionLog;
    #endregion
    /// <summary>
    /// Gets the date and time at which the operation started.
    /// </summary>
    public DateTimeOffset StartedAt { get; }
    /// <summary>
    /// Gets the current runtime configuration for the FTP server.
    /// </summary>
    public AmFtpdRuntimeConfig Runtime => _runtime;
    /// <summary>
    /// Gets the path to the configuration file used by the FTP server.
    /// </summary>
    public string ConfigPath => _configPath;
    /// <summary>
    /// Gets the current <see cref="HammerGuard"/> instance associated with this object.
    /// </summary>
    public HammerGuard HammerGuard => _hammerGuard;
    /// <summary>
    /// Gets the list of banned users associated with the current instance.
    /// </summary>
    public BanList BanList => _banList;
    /// <summary>
    /// Initializes a new instance of the FtpServer class using the specified runtime configuration and logger.
    /// </summary>
    /// <remarks>This constructor sets up the FTP server with the provided configuration and logging
    /// components. The runtime configuration supplies all necessary operational details, while the logger enables
    /// monitoring and troubleshooting of server activity.</remarks>
    /// <param name="runtime">The runtime configuration containing FTP server settings, user store, TLS configuration, and other operational
    /// parameters. Cannot be null.</param>
    /// <param name="log">The logger used to record FTP server events and diagnostics. Cannot be null.</param>
    public FtpServer(
        AmFtpdRuntimeConfig runtime,
        IFtpLogger log)
    {
        _runtime = runtime;
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _configPath = runtime.ConfigFilePath ?? throw new ArgumentNullException(
            nameof(runtime.ConfigFilePath),
            "Runtime configuration must know the path to its JSON source.");

        StartedAt = DateTimeOffset.UtcNow;

        // Initialize session/audit logging (JSONL) next to the config file.
        try
        {
            var configDir = Path.GetDirectoryName(_configPath);
            if (string.IsNullOrWhiteSpace(configDir))
                configDir = AppContext.BaseDirectory;

            var sessionLogPath = Path.Combine(configDir!, "amftpd-sessionlog.jsonl");

            _sessionLog = new SessionLogWriter(sessionLogPath, _log);
            runtime.EventBus.Subscribe(_sessionLog.OnEvent);

            _log.Log(FtpLogLevel.Info, $"Session log enabled. Path: {sessionLogPath}");
        }
        catch (Exception ex)
        {
            _log.Log(
                FtpLogLevel.Warn,
                $"Failed to initialize session log writer; running without session log. {ex.Message}",
                ex);
        }

        // Assign all local fields from runtime
        _cfg = runtime.FtpConfig;
        _users = runtime.UserStore;
        _tls = runtime.TlsConfig;
        _sections = runtime.Sections;
        _identCfg = runtime.IdentConfig;
        _vfsCfg = runtime.VfsConfig;
        _sectionResolver = new SectionResolver(_sections.GetSections());

        _hammerGuard = new HammerGuard(_cfg);
        _banList = new BanList();
    }
    /// <summary>
    /// Starts the FTP(S) server and begins listening for incoming client connections.
    /// </summary>
    /// <remarks>This method initializes the server, binds it to the configured address and port, and begins
    /// accepting incoming TCP client connections. Each client connection is handled in a separate session, which runs
    /// asynchronously. The server continues to listen for connections until it is explicitly stopped by canceling the
    /// associated <see cref="CancellationTokenSource" />. <para> Exceptions encountered during the acceptance of client
    /// connections are logged, and the server continues to operate unless the cancellation token is triggered.
    /// </para></remarks>
    /// <returns></returns>
    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();

        var ip = string.IsNullOrWhiteSpace(_cfg.BindAddress)
            ? IPAddress.Any
            : IPAddress.Parse(_cfg.BindAddress);

        _listener = new TcpListener(new IPEndPoint(ip, (int)_cfg.Port));
        _listener.Start();
        _log.Log(FtpLogLevel.Info, $"Server listening on {_cfg.BindAddress}:{_cfg.Port}");

        // Shared filesystem instance
        var fs = new FtpFileSystem(_cfg.RootPath);

        // --------------------------------------------------------------------
        // AMScript: load script config and ensure default rule-sets
        // --------------------------------------------------------------------
        var baseDir = AppContext.BaseDirectory;
        var scriptCfgPath = Path.Combine(baseDir, "config", "scripts.json");
        var scriptConfig = ScriptConfig.Load(scriptCfgPath);
        _log.Log(FtpLogLevel.Debug, "AMScript initiated and loaded...");

        // Resolve rules base path: absolute stays absolute, relative is based on app dir
        var rulesBase = scriptConfig.RulesPath;
        if (!Path.IsPathRooted(rulesBase))
            rulesBase = Path.GetFullPath(Path.Combine(baseDir, rulesBase));

        AMScriptDefaults.EnsureAll(rulesBase);
        _log.Log(FtpLogLevel.Debug, "AMScript rules set and initiated...");

        var creditScript = new AMScriptEngine(Path.Combine(rulesBase, "credits.msl"));
        var fxpScript = new AMScriptEngine(Path.Combine(rulesBase, "fxp.msl"));
        var activeScript = new AMScriptEngine(Path.Combine(rulesBase, "active.msl"));
        var sectionRoutingScript = new AMScriptEngine(Path.Combine(rulesBase, "section-routing.msl"));
        var siteScript = new AMScriptEngine(Path.Combine(rulesBase, "site.msl"));
        var userScript = new AMScriptEngine(Path.Combine(rulesBase, "user-rules.msl"));
        var groupScript = new AMScriptEngine(Path.Combine(rulesBase, "group-rules.msl"));
        _log.Log(FtpLogLevel.Debug, "All required scripts loaded and initiated...");

        // Optional: pipe AMScript debug into your logger
        creditScript.DebugLog = msg => _log.Log(FtpLogLevel.Debug, msg);
        fxpScript.DebugLog = msg => _log.Log(FtpLogLevel.Debug, msg);
        activeScript.DebugLog = msg => _log.Log(FtpLogLevel.Debug, msg);
        sectionRoutingScript.DebugLog = msg => _log.Log(FtpLogLevel.Debug, msg);
        siteScript.DebugLog = msg => _log.Log(FtpLogLevel.Debug, msg);
        userScript.DebugLog = msg => _log.Log(FtpLogLevel.Debug, msg);
        groupScript.DebugLog = msg => _log.Log(FtpLogLevel.Debug, msg);

        ConfigureScriptEngine(creditScript, scriptConfig);
        ConfigureScriptEngine(fxpScript, scriptConfig);
        ConfigureScriptEngine(activeScript, scriptConfig);
        ConfigureScriptEngine(sectionRoutingScript, scriptConfig);
        ConfigureScriptEngine(siteScript, scriptConfig);
        ConfigureScriptEngine(userScript, scriptConfig);
        ConfigureScriptEngine(groupScript, scriptConfig);

        // --------------------------------------------------------------------
        // Main accept loop
        // --------------------------------------------------------------------
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Error, "Accept failed", ex);
                continue;
            }

            // Determine remote endpoint
            var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            if (remoteEndPoint is null)
            {
                client.Dispose();
                continue;
            }

            // Connection limit / ban checks
            if (!TryRegisterConnection(remoteEndPoint, out var rejectMessage))
            {
                try
                {
                    using var stream = client.GetStream();
                    var msg = string.IsNullOrEmpty(rejectMessage)
                        ? "421 Too many connections, try again later.\r\n"
                        : $"421 {rejectMessage}\r\n";

                    var buf = System.Text.Encoding.ASCII.GetBytes(msg);
                    await stream.WriteAsync(buf.AsMemory(0, buf.Length), _cts.Token);
                }
                catch
                {
                    // best effort
                }

                client.Dispose();
                continue;
            }

            var rem = remoteEndPoint.ToString();
            _log.Log(FtpLogLevel.Info, $"Connection from {rem}");

            // update global perf counters
            PerfCounters.ConnectionOpened();

            var defaultProt = MapDataChannelProtectionToLetter(_cfg.DataChannelProtectionDefault);

            // capture for the task
            var remoteCopy = remoteEndPoint;

            _ = Task.Run(async () =>
            {
                try
                {
                    await using var session = new FtpSession(
                        client,
                        _log,
                        _cfg,
                        _users,
                        fs,
                        defaultProt,
                        _tls,
                        _identCfg,
                        _vfsCfg,
                        _sectionResolver, this);

                    var router = new FtpCommandRouter(this, session, _log, fs, _cfg, _tls, _sections, _runtime);

                    // Attach script engines so router can use AMScript in credits/FXP/active
                    router.AttachScriptEngines(
                        creditScript,
                        fxpScript,
                        activeScript,
                        sectionRoutingScript,
                        siteScript,
                        userScript,
                        groupScript);

                    var ct = _cts!.Token;

                    try
                    {
                        await session.RunAsync(router, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.Log(FtpLogLevel.Error, $"Unhandled session error for {rem}", ex);
                    }

                    _log.Log(FtpLogLevel.Info, $"Connection closed: {rem}");
                }
                finally
                {
                    // Always decrement global / per-IP counters
                    UnregisterConnection(remoteCopy);
                }
            });
        }
    }
    
    private void ConfigureScriptEngine(AMScriptEngine e, ScriptConfig scriptConfig)
    {
        e.MaxRulesPerEvaluation = scriptConfig.MaxRulesPerEvaluation;
        e.MaxEvaluationTime = scriptConfig.MaxEvaluationMilliseconds > 0
            ? TimeSpan.FromMilliseconds(scriptConfig.MaxEvaluationMilliseconds)
            : TimeSpan.Zero;
        e.MaxConcurrentEvaluations = scriptConfig.MaxConcurrentScripts;
    }

    private static string MapDataChannelProtectionToLetter(DataChannelProtectionLevel level)
        => level switch
        {
            DataChannelProtectionLevel.Clear => "C",

            // We don't distinguish Safe/Confidential/Private at the TCP level;
            // anything non-clear is treated as "Private" (TLS on data connection).
            DataChannelProtectionLevel.Safe => "P",
            DataChannelProtectionLevel.Confidential => "P",
            DataChannelProtectionLevel.Private => "P",

            _ => "C"
        };

    private bool TryRegisterConnection(IPEndPoint remoteEndPoint, out string? rejectMessage)
    {
        rejectMessage = null;

        if (_banList.IsBanned(remoteEndPoint.Address, out var banReason))
        {
            rejectMessage = banReason ?? "Access denied.";
            _log.Log(FtpLogLevel.Warn, $"Rejected banned IP {remoteEndPoint.Address}: {rejectMessage}");
            return false;
        }

        lock (_connectionLock)
        {
            var globalMax = _cfg.MaxConnectionsGlobal;
            var perIpMax = _cfg.MaxConnectionsPerIp;

            if (globalMax > 0 && _currentConnections >= globalMax)
            {
                rejectMessage = "Server at connection limit.";
                _log.Log(FtpLogLevel.Warn,
                    $"Rejecting {remoteEndPoint} (global limit reached: {globalMax})");
                return false;
            }

            var ip = remoteEndPoint.Address;

            var newPerIpCount = _connectionsPerIp.AddOrUpdate(ip, 1, (_, current) => current + 1);

            if (perIpMax > 0 && newPerIpCount > perIpMax)
            {
                // revert increment
                _connectionsPerIp.AddOrUpdate(ip, 0, (_, current) => Math.Max(0, current - 1));

                rejectMessage = "Too many connections from your IP.";
                _log.Log(FtpLogLevel.Warn,
                    $"Rejecting {remoteEndPoint} (per IP limit reached: {newPerIpCount}/{perIpMax})");

                return false;
            }

            _currentConnections++;
        }

        return true;
    }

    private void UnregisterConnection(IPEndPoint remoteEndPoint)
    {
        lock (_connectionLock)
        {
            if (_currentConnections > 0)
                _currentConnections--;

            var ip = remoteEndPoint.Address;

            if (_connectionsPerIp.TryGetValue(ip, out var current))
            {
                if (current <= 1)
                {
                    _connectionsPerIp.TryRemove(ip, out _);
                }
                else
                {
                    _connectionsPerIp[ip] = current - 1;
                }
            }
        }

        // Keep the perf counters in sync with the real connection set.
        PerfCounters.ConnectionClosed();
    }

    // Optional helper for login failures from elsewhere:
    public void NotifyFailedLogin(IPAddress address)
    {
        var decision = _hammerGuard.RegisterFailedLogin(address);
        if (decision.ShouldBan && decision.BanDuration.HasValue)
        {
            _banList.AddTemporaryBan(address, decision.BanDuration.Value, decision.Reason);
            _log.Log(FtpLogLevel.Warn,$"IP {address} temporarily banned for {decision.BanDuration} ({decision.Reason})");
        }
    }
    /// <summary>
    /// Reloads the configuration from disk and atomically swaps the live runtime snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the reload operation.</param>
    /// <returns>
    /// A tuple indicating success, a human-readable summary, and the list of high-level sections that changed.
    /// </returns>
    public async Task<(bool Success, string Message, IReadOnlyList<string> ChangedSections)>
        ReloadConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await AmFtpdConfigLoader.ReloadAsync(
                    _configPath,
                    _runtime,
                    _log,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (_configLock)
            {
                _runtime = result.Runtime;

                _cfg = _runtime.FtpConfig;
                _users = _runtime.UserStore;
                _tls = _runtime.TlsConfig;

                _sections = _runtime.Sections;
                _sectionResolver = new SectionResolver(_sections.GetSections());

                _identCfg = _runtime.IdentConfig;
                _vfsCfg = _runtime.VfsConfig;

                // Recreate HammerGuard with new thresholds,
                // while keeping the existing BanList (bans survive reload).
                _hammerGuard = new HammerGuard(_cfg);
            }

            var msg = "Configuration reloaded successfully.";
            if (result.ChangedSections.Count > 0)
            {
                msg += " Changed sections: " + string.Join(", ", result.ChangedSections);
            }
            else
            {
                msg += " No material changes detected.";
            }

            _log.Log(FtpLogLevel.Info, $"[REHASH] {msg}");

            return (true, msg, result.ChangedSections);
        }
        catch (Exception ex)
        {
            var err = $"Configuration reload failed: {ex.Message}";
            _log.Log(FtpLogLevel.Error, err, ex);
            return (false, err, Array.Empty<string>());
        }
    }
    /// <summary>
    /// Stops the FTP server, canceling any ongoing operations and releasing resources.
    /// </summary>
    /// <remarks>This method cancels any pending tasks, stops the listener, and logs the server shutdown
    /// event.  Once called, the server will no longer accept new connections or process requests.</remarks>
    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _log.Log(FtpLogLevel.Info, "Server stopped.");
    }
}