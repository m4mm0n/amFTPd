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
using amFTPd.Config.Scripting;
using amFTPd.Core.Irc;
using amFTPd.Core.Monitoring;
using amFTPd.Core.Pre;
using amFTPd.Core.ReleaseSystem;
using amFTPd.Core.Runtime;
using amFTPd.Core.Scene;
using amFTPd.Core.Sections;
using amFTPd.Core.Services;
using amFTPd.Core.Stats;
using amFTPd.Core.Stats.Live;
using amFTPd.Db;
using amFTPd.Logging;
using amFTPd.Scripting;
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
    //private FtpConfig _cfg;
    //private IUserStore _users;
    //private TlsConfig _tls;
    private readonly IFtpLogger _log;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    private IrcAnnouncer? _irc;
    //private SectionManager _sections;

    //private IdentConfig _identCfg;
    //private VfsConfig _vfsCfg;
    //private SectionResolver _sectionResolver;

    private HammerGuard _hammerGuard;
    private readonly BanList _banList;

    private readonly Lock _connectionLock = new();
    // Active connections per IP
    private readonly ConcurrentDictionary<IPAddress, int> _connectionsPerIp = new();
    // Commands seen per IP (rolling, policy applied later)
    private readonly ConcurrentDictionary<IPAddress, long> _commandsPerIp = new();
    private int _currentConnections;

    private AmFtpdRuntimeConfig _runtime;
    private readonly string _configPath;
    private readonly Lock _configLock = new();
    private readonly Lock _monitoringLock = new();

    // Release registry persistence (debounced) - avoids losing all release state on crash.
    private Timer? _releasePersistTimer;
    private int _releasePersistPending;
    private DateTimeOffset _lastReleasePersistUtc;
    private string? _releaseRegistryPath;
    private static readonly TimeSpan ReleasePersistDebounce = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReleasePersistMinInterval = TimeSpan.FromSeconds(10);


    // AMScript engines (hot-reloadable via REHASH)
    private ScriptEngines? _scripts;
    private string? _scriptConfigPath;
    private ScriptConfig? _scriptConfig;
    private string? _rulesBasePath;

    private readonly SessionLogWriter? _sessionLog;

    private readonly StatsCollector _stats =
        new(TimeSpan.FromSeconds(1));

    private readonly TimeSpan _defaultBlockDuration = TimeSpan.FromMinutes(10);

    private const int MaxSessionsPerIp = 5;

    // command flood
    private const int IpCommandWindowSeconds = 5;
    private const int MaxCommandsPerIpWindow = 40;

    // failed logins
    private const int MaxFailedLoginsPerIp = 8;

    // bandwidth (5 sec window)
    private const int BandwidthWindowSeconds = 5;
    private const long MaxBytesPerIpWindow = 50 * 1024 * 1024; // 50 MB / 5s

    // section bandwidth (5 sec window)
    private const int SectionBandwidthWindowSeconds = 5;
    private const long MaxBytesPerSectionWindow = 20 * 1024 * 1024; // 20MB/5s

    private HousekeepingService? _housekeeping;

    #endregion
    /// <summary>
    /// Gets the date and time at which the operation started.
    /// </summary>
    public DateTimeOffset StartedAt { get; }
    /// <summary>
    /// Gets the current runtime configuration for the FTP server.
    /// </summary>
    public AmFtpdRuntimeConfig Runtime => Volatile.Read(ref _runtime);
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
    /// Gets the statistics collector associated with this instance.
    /// </summary>
    public StatsCollector Stats => _stats;
    /// <summary>
    /// Gets the status endpoint configuration for the service, if available.
    /// </summary>
    public StatusEndpoint? StatusEndpoint { get; internal set; }
    /// <summary>Gets the Prometheus metrics endpoint, if running.</summary>
    public MetricsEndpoint? MetricsEndpoint { get; internal set; }
    /// <summary>
    /// Gets the registry that manages the state of the scene releases.
    /// </summary>
    public SceneStateRegistry SceneRegistry { get; }

    /// <summary>
    /// Initializes a new instance of the FtpServer class using the specified runtime configuration and logger.
    /// </summary>
    /// <remarks>This constructor sets up the FTP server with the provided configuration and logging
    /// components. The runtime configuration supplies all necessary operational details, while the logger enables
    /// monitoring and troubleshooting of server activity.</remarks>
    /// <param name="runtime">The runtime configuration containing FTP server settings, user store, TLS configuration, and other operational
    /// parameters. Cannot be null.</param>
    /// <param name="log">The logger used to record FTP server events and diagnostics. Cannot be null.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public FtpServer(
        AmFtpdRuntimeConfig runtime,
        IFtpLogger log)
    {
        _runtime = runtime;
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _configPath = runtime.ConfigFilePath ?? throw new ArgumentNullException(
            nameof(runtime.ConfigFilePath),
            "Runtime configuration must know the path to its JSON source.");

        _housekeeping = new HousekeepingService(
            Runtime,
            _log);

        StartedAt = DateTimeOffset.UtcNow;

        // Initialize session/audit logging (JSONL) next to the config file.
        try
        {
            var configDir = Path.GetDirectoryName(_configPath);
            if (string.IsNullOrWhiteSpace(configDir))
                configDir = AppContext.BaseDirectory;

            var sessionLogPath = Path.Combine(configDir!, "amftpd-sessionlog.jsonl");

            _sessionLog = new(sessionLogPath, _log);
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

        _hammerGuard = new(Runtime.FtpConfig);
        _banList = new();
        Runtime.Recovery = new RuntimeRecoveryManager(Runtime);

        // Initialize IRC announcer if configured
        if (Runtime.IrcConfig is { Enabled: true } ircCfg)
        {
            if (ircCfg.FishEnabled && ircCfg.FishKeys.Count > 0)
            {
                _log.Log(FtpLogLevel.Info,
                    $"[IRC] FiSH enabled ({ircCfg.FishKeys.Count} key(s))");
            }
            else
            {
                _log.Log(FtpLogLevel.Info,
                    "[IRC] FiSH disabled or no keys configured");
            }

            _irc = new IrcAnnouncer(
                ircCfg,
                _log,
                Runtime.EventBus,
                scriptHook: null);
        }

        SceneRegistry = new SceneStateRegistry();

        Runtime.Zipscript.ReleaseUpdated += status =>
        {
            try
            {
                var releaseName = Path.GetFileName(status.ReleasePath);

                var ctx = Runtime.ReleaseRegistry
                    .GetOrCreate(status.SectionName, releaseName);

                // timestamps (authoritative)
                if (ctx.FirstSeen == default)
                    ctx.FirstSeen = status.Started;

                ctx.LastUpdated = status.LastUpdated;

                // lifecycle state
                ctx.State =
                    status.IsNuked
                        ? ReleaseState.Nuked
                        : status.IsComplete
                            ? ReleaseState.Complete
                            : ReleaseState.Incomplete;

                // ----------------------------
                // DERIVED DATA (IMPORTANT)
                // ----------------------------
                long totalBytes = 0;
                var hasNfo = false;
                var hasDiz = false;

                foreach (var f in status.Files)
                {
                    totalBytes += f.SizeBytes;

                    if (f.FileName.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase))
                        hasNfo = true;
                    else if (f.FileName.EndsWith(".diz", StringComparison.OrdinalIgnoreCase))
                        hasDiz = true;
                }

                ctx.FileCount = status.Files.Count;
                ctx.TotalBytes = totalBytes;
                ctx.HasSfv = status.HasSfv;
                ctx.HasNfo = hasNfo;
                ctx.HasDiz = hasDiz;

                // Persist release state (debounced) so crashes don't wipe the in-memory registry.
                ScheduleReleaseRegistrySave();
            }
            catch (Exception ex)
            {
                _log.Log(
                    FtpLogLevel.Warn,
                    "[RELEASE] Failed to update release registry",
                    ex);
            }
        };
        Runtime.Zipscript.PreDetected += pre =>
        {
            Runtime.PreRegistry.TryAdd(new PreEntry(
                pre.SectionName,
                pre.ReleaseName,
                pre.VirtualReleasePath,
                pre.UserName,
                pre.Timestamp));
        };
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
        _cts = new();

        var ip = string.IsNullOrWhiteSpace(Runtime.FtpConfig
            .BindAddress)
            ? IPAddress.Any
            : IPAddress.Parse(Runtime.FtpConfig.BindAddress);

        Runtime.Recovery.BeginRecovery();
        Runtime.Recovery.LoadAll();
        Runtime.Recovery.EndRecovery();

        var sceneFile = Path.Combine(
            Path.GetDirectoryName(Runtime.ConfigFilePath)!,
            "scene_registry.json");

        var scenePersistence = new SceneRegistryPersistence();
        scenePersistence.Load(SceneRegistry, sceneFile);

        // Persisted release registry (lightweight recovery)
        try
        {
            _releaseRegistryPath = ComputeReleaseRegistryPath(Runtime);

            new ReleaseRegistryPersistence()
                .Load(Runtime.ReleaseRegistry, _releaseRegistryPath);
        }
        catch (Exception ex)
        {
            _log.Log(
                FtpLogLevel.Warn,
                $"Release registry persistence failed during startup: {ex.Message}",
                ex);
        }

        _listener = new(new IPEndPoint(ip, Runtime.FtpConfig.Port));
        _listener.Start();
        _log.Log(FtpLogLevel.Info, $"Server listening on {Runtime.FtpConfig.BindAddress}:{Runtime.FtpConfig.Port}");

        if (Runtime.IrcConfig is { Enabled: true })
        {
            _irc?.Start();
            _log.Log(FtpLogLevel.Info, "[IRC] IRC announcer started.");
        }

        // Shared filesystem instance
        var fs = new FtpFileSystem(Runtime.FtpConfig.RootPath);

        ReloadScriptsForCurrentConfig();

        // Start/refresh monitoring endpoints (status/metrics)
        RestartMonitoringEndpoints(Runtime);

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
                    await using var stream = client.GetStream();
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

            var defaultProt = MapDataChannelProtectionToLetter(Runtime.FtpConfig.DataChannelProtectionDefault);

            // capture for the task
            var remoteCopy = remoteEndPoint;

            _ = Task.Run(async () =>
            {
                try
                {
                    await using var session = new FtpSession(
                        client,
                        _log,
                        Runtime.FtpConfig,
                        Runtime.UserStore,
                        fs,
                        defaultProt,
                        Runtime.TlsConfig,
                        Runtime.IdentConfig,
                        Runtime.VfsConfig,
                        new SectionResolver(Runtime.Sections.GetSections()), this);

                    var router = new FtpCommandRouter(this, session, _log, fs, Runtime.FtpConfig, Runtime.TlsConfig, Runtime.Sections, Runtime);

                    // Attach script engines so router can use AMScript in credits/FXP/active
                    ScriptEngines? scripts;
                    lock (_configLock)
                    {
                        scripts = _scripts;
                    }
                    router.AttachScriptEngines(
                        scripts?.Credit,
                        scripts?.Fxp,
                        scripts?.Active,
                        scripts?.SectionRouting,
                        scripts?.Site,
                        scripts?.Users,
                        scripts?.Groups);


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

    /// <summary>
    /// Registers a command execution for the specified IP address.
    /// </summary>
    /// <remarks>This method increments the count of commands associated with the given IP address. If the IP
    /// address has not previously been registered, it is added with an initial count of one. This method is
    /// thread-safe.</remarks>
    /// <param name="ip">The IP address for which to register the command. Cannot be null.</param>
    public void RegisterCommand(IPAddress ip) => _commandsPerIp.AddOrUpdate(ip, 1, (_, v) => v + 1);
    /// <summary>
    /// Gets the number of active connections associated with the specified IP address.
    /// </summary>
    /// <param name="ip">The IP address for which to retrieve the count of active connections. Cannot be null.</param>
    /// <returns>The number of active connections for the specified IP address. Returns 0 if there are no active connections for
    /// the given address.</returns>
    public int GetActiveConnections(IPAddress ip) => _connectionsPerIp.GetValueOrDefault(ip, 0);

    /// <summary>
    /// Gets the total number of commands received from the specified IP address.
    /// </summary>
    /// <param name="ip">The IP address for which to retrieve the command count. Cannot be null.</param>
    /// <returns>The number of commands received from the specified IP address. Returns 0 if the IP address has not sent any
    /// commands.</returns>
    public long GetCommandCount(IPAddress ip) => _commandsPerIp.GetValueOrDefault(ip, 0);

    internal AmFtpdRuntimeConfig SwapRuntime(AmFtpdRuntimeConfig newRuntime) => Interlocked.Exchange(ref _runtime, newRuntime);



    private sealed record ScriptEngines(
        AMScriptEngine Credit,
        AMScriptEngine Fxp,
        AMScriptEngine Active,
        AMScriptEngine SectionRouting,
        AMScriptEngine Site,
        AMScriptEngine Users,
        AMScriptEngine Groups) : IDisposable
    {
        public void Dispose()
        {
            Credit.Dispose();
            Fxp.Dispose();
            Active.Dispose();
            SectionRouting.Dispose();
            Site.Dispose();
            Users.Dispose();
            Groups.Dispose();
        }
    }



    private void RestartMonitoringEndpoints(AmFtpdRuntimeConfig runtime)
    {
        lock (_monitoringLock)
        {
            // Stop existing endpoints
            if (StatusEndpoint is not null)
            {
                try { StatusEndpoint.DisposeAsync().AsTask().Wait(); } catch { }
                StatusEndpoint = null;
            }

            if (MetricsEndpoint is not null)
            {
                try { MetricsEndpoint.DisposeAsync().AsTask().Wait(); } catch { }
                MetricsEndpoint = null;
            }

            var cfg = runtime.StatusConfig;
            if (cfg is null || !cfg.Enabled)
                return;

            try
            {
                var status = new StatusEndpoint(runtime, _log, cfg);
                status.Start();
                StatusEndpoint = status;
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn, "[Status] Failed to start status endpoint.", ex);
            }

            try
            {
                if (cfg.MetricsEnabled)
                {
                    var port = cfg.MetricsPort ?? (cfg.Port + 1);
                    var metrics = new MetricsEndpoint(runtime, _log, cfg.BindAddress, port);
                    metrics.Start();
                    MetricsEndpoint = metrics;
                }
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn, "[Metrics] Failed to start metrics endpoint.", ex);
            }
        }
    }
    private void ReloadScriptsForCurrentConfig()
    {
        // After Runtime + stores are initialized
        var baseDir = Path.GetDirectoryName(Runtime.ConfigFilePath)
                      ?? AppContext.BaseDirectory;

        var scriptCfgPath = Path.Combine(baseDir, "config", "scripts.json");
        var scriptConfig = ScriptConfig.Load(scriptCfgPath);

        // Resolve rules base path: absolute stays absolute, relative is based on app dir
        var rulesBase = scriptConfig.RulesPath;
        if (!Path.IsPathRooted(rulesBase))
            rulesBase = Path.GetFullPath(Path.Combine(baseDir, rulesBase));

        AMScriptDefaults.EnsureAll(rulesBase);

        var creditScript = new AMScriptEngine(Path.Combine(rulesBase, "credits.msl"));
        var fxpScript = new AMScriptEngine(Path.Combine(rulesBase, "fxp.msl"));
        var activeScript = new AMScriptEngine(Path.Combine(rulesBase, "active.msl"));
        var sectionRoutingScript = new AMScriptEngine(Path.Combine(rulesBase, "section-routing.msl"));
        var siteScript = new AMScriptEngine(Path.Combine(rulesBase, "site.msl"));
        var userScript = new AMScriptEngine(Path.Combine(rulesBase, "user-rules.msl"));
        var groupScript = new AMScriptEngine(Path.Combine(rulesBase, "group-rules.msl"));

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

        var newScripts = new ScriptEngines(
            creditScript,
            fxpScript,
            activeScript,
            sectionRoutingScript,
            siteScript,
            userScript,
            groupScript);

        ScriptEngines? old;
        lock (_configLock)
        {
            _scriptConfigPath = scriptCfgPath;
            _scriptConfig = scriptConfig;
            _rulesBasePath = rulesBase;

            old = _scripts;
            _scripts = newScripts;
        }

        old?.Dispose();

        _log.Log(FtpLogLevel.Debug, "[AMScript] Script engines loaded/reloaded.");
    }
    private void ConfigureScriptEngine(AMScriptEngine e, ScriptConfig scriptConfig)
    {
        e.MaxRulesPerEvaluation = scriptConfig.MaxRulesPerEvaluation;
        e.MaxEvaluationTime = scriptConfig.MaxEvaluationMilliseconds > 0
            ? TimeSpan.FromMilliseconds(scriptConfig.MaxEvaluationMilliseconds)
            : TimeSpan.Zero;
        e.MaxConcurrentEvaluations = scriptConfig.MaxConcurrentScripts;
    }

    private string ComputeReleaseRegistryPath(AmFtpdRuntimeConfig runtime)
        => Path.Combine(
            Path.GetDirectoryName(runtime.ConfigFilePath) ?? AppContext.BaseDirectory,
            "release_registry.json");

    private void ScheduleReleaseRegistrySave()
    {
        // Path is initialized in StartAsync after Runtime.ConfigFilePath is known.
        if (string.IsNullOrWhiteSpace(_releaseRegistryPath))
            return;

        Interlocked.Exchange(ref _releasePersistPending, 1);

        if (_releasePersistTimer is null)
        {
            _releasePersistTimer = new Timer(
                _ => FlushReleaseRegistryIfPending(),
                state: null,
                dueTime: ReleasePersistDebounce,
                period: Timeout.InfiniteTimeSpan);
            return;
        }

        // Debounce bursts of updates.
        _releasePersistTimer.Change(ReleasePersistDebounce, Timeout.InfiniteTimeSpan);
    }

    private void FlushReleaseRegistryIfPending()
    {
        if (Interlocked.Exchange(ref _releasePersistPending, 0) == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        if (_lastReleasePersistUtc != default && (now - _lastReleasePersistUtc) < ReleasePersistMinInterval)
        {
            // Too soon; reschedule with the remainder (best effort).
            Interlocked.Exchange(ref _releasePersistPending, 1);
            var remaining = ReleasePersistMinInterval - (now - _lastReleasePersistUtc);
            if (remaining < TimeSpan.FromSeconds(1))
                remaining = TimeSpan.FromSeconds(1);
            try { _releasePersistTimer?.Change(remaining, Timeout.InfiniteTimeSpan); }
            catch { }
            return;
        }

        var path = _releaseRegistryPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            new ReleaseRegistryPersistence().Save(Runtime.ReleaseRegistry, path);
            _lastReleasePersistUtc = now;
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Warn, $"Release registry persistence flush failed: {ex.Message}", ex);
        }
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
            var globalMax = Runtime.FtpConfig.MaxConnectionsGlobal;
            var perIpMax = Runtime.FtpConfig.MaxConnectionsPerIp;

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
    private void DecayIpReputation(IpLiveStats ip, DateTimeOffset now)
    {
        if (ip.Reputation == FtpSessionReputation.Good)
            return;

        var suspectCooldown = TimeSpan.FromMinutes(15);
        var blockCooldown = TimeSpan.FromMinutes(60);

        if (ip.Reputation == FtpSessionReputation.Blocked)
        {
            if (ip.BlockedUntilUtc.HasValue && now >= ip.BlockedUntilUtc.Value)
            {
                ip.Reputation = FtpSessionReputation.Suspect;
                ip.BlockedUntilUtc = null;
                ip.LastViolationUtc = now;
            }
            return;
        }

        if (ip.Reputation == FtpSessionReputation.Suspect &&
            now - ip.LastViolationUtc >= suspectCooldown)
        {
            ip.Reputation = FtpSessionReputation.Good;
        }
    }

    // Optional helper for login failures from elsewhere:
    public void NotifyFailedLogin(IPAddress address)
    {
        var decision = HammerGuard.RegisterFailedLogin(address);
        if (decision.ShouldBan && decision.BanDuration.HasValue)
        {
            _banList.AddTemporaryBan(address, decision.BanDuration.Value, decision.Reason);
            _log.Log(FtpLogLevel.Warn, $"IP {address} temporarily banned for {decision.BanDuration} ({decision.Reason})");
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
        var oldRuntime = Runtime;
        var oldHammerGuard = HammerGuard;

        try
        {
            var result = await AmFtpdConfigLoader.ReloadAsync(
                _configPath,
                Runtime,
                _log,
                cancellationToken
            ).ConfigureAwait(false);

            lock (_configLock)
            {
                // Swap runtime atomically
                SwapRuntime(result.Runtime);

                // Recreate HammerGuard with new config
                _hammerGuard = new HammerGuard(Runtime.FtpConfig);
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

            // IRC is the only runtime component that actually needs reload handling
            if (_irc is not null)
            {
                await _irc.ReloadAsync(Runtime.IrcConfig)
                    .ConfigureAwait(false);
            }


            try
            {
                ReloadScriptsForCurrentConfig();
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn, $"[AMScript] Script reload failed: {ex.Message}", ex);
                msg += " (AMScript reload failed; see logs)";
            }

            // Monitoring endpoints must follow the new runtime snapshot
            try
            {
                RestartMonitoringEndpoints(Runtime);
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn, $"[Status] Monitoring refresh failed: {ex.Message}", ex);
                msg += " (monitoring refresh failed; see logs)";
            }

            // Update persistence path (in case ConfigFilePath changes) and flush soon.
            _releaseRegistryPath = ComputeReleaseRegistryPath(Runtime);
            ScheduleReleaseRegistrySave();

            _log.Log(FtpLogLevel.Info, $"[REHASH] {msg}");
            return (true, msg, result.ChangedSections);
        }
        catch (Exception ex)
        {
            lock (_configLock)
            {
                SwapRuntime(oldRuntime);
                _hammerGuard = oldHammerGuard;
            }

            var err = $"Configuration reload failed: {ex.Message}";
            _log.Log(FtpLogLevel.Error, err, ex);
            return (false, err, Array.Empty<string>());
        }
    }

    /// <summary>
    /// Determines whether the specified IP address is currently blocked and provides the reason if it is blocked.
    /// </summary>
    /// <param name="ipKey">The key representing the IP address to check. This should be in the format used by the IP tracking system.
    /// Cannot be null.</param>
    /// <param name="reason">When this method returns, contains the reason the IP address is blocked if it is blocked; otherwise, null.</param>
    /// <returns>true if the specified IP address is currently blocked; otherwise, false.</returns>
    public bool IsIpBlocked(string ipKey, out string? reason)
    {
        reason = null;

        if (!Runtime.LiveStats.Ips.TryGetValue(ipKey, out var ip))
            return false;

        if (!ip.IsCurrentlyBlocked)
            return false;

        reason = ip.BlockReason;
        return true;
    }

    /// <summary>
    /// Evaluates the specified IP address key and blocks the IP if it exceeds allowed session limits.
    /// </summary>
    /// <remarks>This method checks the number of active sessions for the given IP address and blocks it if
    /// the session count exceeds the configured maximum. If the IP is already blocked or not found, no action is
    /// taken.</remarks>
    /// <param name="ipKey">The key representing the IP address to evaluate. Cannot be null.</param>
    public void EvaluateIp(string ipKey)
    {
        if (!Runtime.LiveStats.Ips.TryGetValue(ipKey, out var ip))
            return;

        DecayIpReputation(ip, DateTimeOffset.UtcNow);

        if (ip.IsCurrentlyBlocked)
            return;

        // RULE 1: too many sessions
        if (ip.ActiveSessions > MaxSessionsPerIp)
        {
            BlockIp(
                ip,
                $"too many sessions ({ip.ActiveSessions})");
        }
    }

    /// <summary>
    /// Blocks the specified IP address and records the reason for the block.
    /// </summary>
    /// <param name="ip">The IP statistics object representing the address to block. Cannot be null.</param>
    /// <param name="reason">The reason for blocking the IP address. This value is recorded for auditing purposes.</param>
    public void BlockIp(IpLiveStats ip, string reason)
    {
        ip.IsBlocked = true;
        ip.BlockReason = reason;
        ip.BlockedUntilUtc = DateTimeOffset.UtcNow + _defaultBlockDuration;

        _log.Log(
            FtpLogLevel.Warn,
            $"[ENFORCE] IP bucket {ip.Ip} blocked: {reason}");
    }

    /// <summary>
    /// Removes the block status from the IP address identified by the specified key, allowing it to resume normal
    /// activity.
    /// </summary>
    /// <remarks>If the specified IP address is not currently tracked, this method performs no action. Use
    /// this method to manually restore access for an IP that was previously blocked.</remarks>
    /// <param name="ipKey">The unique key that identifies the IP address to unblock. Cannot be null or empty.</param>
    public void UnblockIp(string ipKey)
    {
        if (!Runtime.LiveStats.Ips.TryGetValue(ipKey, out var ip))
            return;

        ip.IsBlocked = false;
        ip.BlockReason = null;
        ip.BlockedUntilUtc = null;

        _log.Log(
            FtpLogLevel.Info,
            $"[ENFORCE] IP bucket {ipKey} manually unblocked");
    }

    /// <summary>
    /// Removes the blocked status from IP addresses whose block period has expired.
    /// </summary>
    /// <remarks>This method iterates through all tracked IP addresses and unblocks any that are currently
    /// blocked but whose block expiration time has passed. It should be called periodically to ensure that expired IP
    /// blocks are cleared and that IP addresses are not blocked longer than intended.</remarks>
    public void CleanupExpiredIpBlocks()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var ip in Runtime.LiveStats.Ips.Values)
        {
            if (ip.IsBlocked &&
                ip.BlockedUntilUtc <= now)
            {
                ip.IsBlocked = false;
                ip.BlockReason = null;
                ip.BlockedUntilUtc = null;
            }
        }
    }

    /// <summary>
    /// Evaluates the command rate for the specified IP address and blocks the IP if it exceeds the allowed command
    /// threshold within the configured time window.
    /// </summary>
    /// <remarks>This method should be called each time a command is received from an IP address. If the
    /// number of commands from the IP exceeds the maximum allowed within the configured window, the IP will be blocked.
    /// The evaluation is based on a sliding time window and is not thread-safe; concurrent calls for the same IP may
    /// result in race conditions.</remarks>
    /// <param name="ipKey">The unique key representing the IP address to evaluate. Cannot be null or empty.</param>
    public void EvaluateIpCommandRate(string ipKey)
    {
        if (!Runtime.LiveStats.Ips.TryGetValue(ipKey, out var ip))
            return;

        DecayIpReputation(ip, DateTimeOffset.UtcNow);

        if (ip.IsCurrentlyBlocked)
            return;

        var now = DateTimeOffset.UtcNow;

        // Reset window if expired
        if ((now - ip.LastCommandWindowUtc).TotalSeconds > IpCommandWindowSeconds)
        {
            ip.CommandsLastWindow = 0;
            ip.LastCommandWindowUtc = now;
        }

        ip.CommandsLastWindow++;

        if (ip.CommandsLastWindow > MaxCommandsPerIpWindow)
        {
            BlockIp(
                ip,
                $"command flood ({ip.CommandsLastWindow}/{IpCommandWindowSeconds}s)");
        }
    }

    /// <summary>
    /// Records a failed login attempt for the specified IP address and blocks the IP if the maximum allowed failed
    /// attempts is exceeded.
    /// </summary>
    /// <remarks>If the number of failed login attempts for the IP address reaches the configured maximum, the
    /// IP will be blocked from further login attempts. This method has no effect if the IP address is not
    /// tracked.</remarks>
    /// <param name="ipKey">The unique key representing the IP address for which the failed login attempt is to be recorded. Cannot be null
    /// or empty.</param>
    public void NotifyIpLoginFailed(string ipKey)
    {
        if (!Runtime.LiveStats.Ips.TryGetValue(ipKey, out var ip))
            return;

        ip.FailedLogins++;
        ip.LastFailedLoginUtc = DateTimeOffset.UtcNow;

        if (!ip.IsCurrentlyBlocked &&
            ip.FailedLogins >= MaxFailedLoginsPerIp)
        {
            BlockIp(ip, $"failed logins ({ip.FailedLogins})");
        }
    }

    /// <summary>
    /// Notifies the system of bandwidth usage for a specific IP address, updating usage statistics and enforcing
    /// bandwidth limits as necessary.
    /// </summary>
    /// <remarks>If the IP address is currently blocked or not found, this method has no effect. Bandwidth
    /// usage is tracked in both short and long time windows to detect and enforce limits, including blocking IPs that
    /// exceed thresholds or exhibit suspicious download/upload ratios.</remarks>
    /// <param name="ipKey">The unique key identifying the IP address for which bandwidth usage is being reported. Cannot be null or empty.</param>
    /// <param name="bytes">The number of bytes transferred during the operation. Must be zero or greater.</param>
    /// <param name="isUpload">A value indicating whether the bytes represent uploaded data. Set to <see langword="true"/> for upload traffic;
    /// otherwise, <see langword="false"/> for download traffic.</param>
    public void NotifyIpBandwidth(
        string ipKey,
        long bytes,
        bool isUpload)
    {
        if (!Runtime.LiveStats.Ips.TryGetValue(ipKey, out var ip))
            return;

        DecayIpReputation(ip, DateTimeOffset.UtcNow);

        if (ip.IsCurrentlyBlocked)
            return;

        var now = DateTimeOffset.UtcNow;

        // --------------------------------------------------------------
        // Short window (existing enforcement)
        // --------------------------------------------------------------
        if ((now - ip.BandwidthWindowUtc).TotalSeconds > BandwidthWindowSeconds)
        {
            ip.BytesWindow = 0;
            ip.BandwidthWindowUtc = now;
        }

        ip.BytesWindow += bytes;

        // --------------------------------------------------------------
        // Long window (ratio tracking)
        // --------------------------------------------------------------
        if ((now - ip.RatioWindowUtc).TotalMinutes >= 5)
        {
            ip.UploadBytes5m = 0;
            ip.DownloadBytes5m = 0;
            ip.RatioWindowUtc = now;
        }

        if (isUpload)
        {
            ip.BytesUploaded += bytes;
            ip.UploadBytes5m += bytes;
        }
        else
        {
            ip.BytesDownloaded += bytes;
            ip.DownloadBytes5m += bytes;
        }

        // --------------------------------------------------------------
        // Hard bandwidth abuse (existing logic)
        // --------------------------------------------------------------
        if (ip.BytesWindow > MaxBytesPerIpWindow)
        {
            BlockIp(
                ip,
                $"bandwidth abuse ({ip.BytesWindow / 1024 / 1024}MB/{BandwidthWindowSeconds}s)");
            return;
        }

        // --------------------------------------------------------------
        // Ratio-based abuse detection (NEW)
        // --------------------------------------------------------------
        const double suspectRatio = 50.0;
        const double blockRatio = 200.0;
        const long minDownloadForEval = 100L * 1024 * 1024; // 100 MB

        if (ip.DownloadBytes5m >= minDownloadForEval &&
            ip.UploadBytes5m > 0)
        {
            var ratio =
                (double)ip.DownloadBytes5m /
                Math.Max(1, ip.UploadBytes5m);

            if (ratio >= blockRatio)
            {
                BlockIp(
                    ip,
                    $"download/upload ratio abuse ({ratio:0.0}:1)");
            }
            else if (ratio >= suspectRatio)
            {
                _log.Log(
                    FtpLogLevel.Warn,
                    $"[SUSPECT] IP bucket {ipKey} high DL/UL ratio {ratio:0.0}:1");

                foreach (var s in FtpSession.GetActiveSessions())
                {
                    if (s.Server == this &&
                        s.Reputation == FtpSessionReputation.Good &&
                        s.TryGetIpKey(out var skey) &&
                        skey == ipKey)
                    {
                        s.Reputation = FtpSessionReputation.Suspect;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Notifies the system of bandwidth usage for a specified section, updating usage statistics and enforcing
    /// bandwidth limits as necessary.
    /// </summary>
    /// <remarks>If the section exceeds its configured bandwidth window, it may be blocked from further
    /// transfers until the block duration expires. Sections that are not found or are already blocked are
    /// ignored.</remarks>
    /// <param name="sectionName">The name of the section for which bandwidth usage is being reported. This value identifies the section whose
    /// statistics will be updated.</param>
    /// <param name="bytes">The number of bytes transferred during the operation. This value is added to the section's bandwidth usage
    /// counters.</param>
    /// <param name="isUpload">A value indicating whether the bytes represent uploaded data. Set to <see langword="true"/> for uploads;
    /// otherwise, <see langword="false"/> for downloads.</param>
    public void NotifySectionBandwidth(
        string sectionName,
        long bytes,
        bool isUpload)
    {
        if (!Runtime.LiveStats.Sections.TryGetValue(sectionName, out var sec))
            return;

        if (sec.IsCurrentlyBlocked)
            return;

        var now = DateTimeOffset.UtcNow;

        // ------------------------------------------------------------
        // Rolling window
        // ------------------------------------------------------------
        if ((now - sec.BandwidthWindowUtc).TotalSeconds > SectionBandwidthWindowSeconds)
        {
            sec.BytesWindow = 0;
            sec.BandwidthWindowUtc = now;
        }

        sec.BytesWindow += bytes;

        if (isUpload)
            sec.BytesUploaded += bytes;
        else
            sec.BytesDownloaded += bytes;

        // ------------------------------------------------------------
        // Enforcement
        // ------------------------------------------------------------
        if (sec.BytesWindow > MaxBytesPerSectionWindow)
        {
            sec.IsBlocked = true;
            sec.BlockReason =
                $"section bandwidth abuse ({sec.BytesWindow / 1024 / 1024}MB/{SectionBandwidthWindowSeconds}s)";
            sec.BlockedUntilUtc =
                DateTimeOffset.UtcNow + _defaultBlockDuration;

            _log.Log(
                FtpLogLevel.Warn,
                $"[ENFORCE] Section {sectionName} blocked: {sec.BlockReason}");
        }
    }

    /// <summary>
    /// Blocks the IP address associated with the specified key and records the provided reason.
    /// </summary>
    /// <param name="ipKey">The unique key identifying the IP address to block. Cannot be null.</param>
    /// <param name="reason">The reason for blocking the IP address. This information may be logged or displayed to users.</param>
    public void BlockIpByKey(string ipKey, string reason)
    {
        if (!Runtime.LiveStats.Ips.TryGetValue(ipKey, out var ip))
            return;

        BlockIp(ip, reason);
    }

    /// <summary>
    /// Determines whether the specified section is currently blocked and provides the reason if it is.
    /// </summary>
    /// <param name="sectionName">The name of the section to check for a blocked status. Cannot be null.</param>
    /// <param name="reason">When this method returns, contains the reason the section is blocked if it is blocked; otherwise, null.</param>
    /// <returns>true if the section is currently blocked; otherwise, false.</returns>
    public bool IsSectionBlocked(string sectionName, out string? reason)
    {
        reason = null;

        if (!Runtime.LiveStats.Sections.TryGetValue(sectionName, out var sec))
            return false;

        if (!sec.IsCurrentlyBlocked)
            return false;

        reason = sec.BlockReason;
        return true;
    }

    /// <summary>
    /// Stops the FTP server, canceling any ongoing operations and releasing resources.
    /// </summary>
    /// <remarks>This method cancels any pending tasks, stops the listener, and logs the server shutdown
    /// event.  Once called, the server will no longer accept new connections or process requests.</remarks>
    public void Stop()
    {
        try
        {
            _releasePersistTimer?.Dispose();
        }
        catch { }
        _releasePersistTimer = null;
        Interlocked.Exchange(ref _releasePersistPending, 0);

        try
        {
            // Recovery is optional depending on startup state
            Runtime.Recovery?.SaveAll();
        }
        catch (Exception ex)
        {
            _log.Log(
                FtpLogLevel.Warn,
                $"Recovery save failed during shutdown: {ex.Message}",
                ex);
        }

        try
        {
            var sceneFile = Path.Combine(
                Path.GetDirectoryName(Runtime.ConfigFilePath)!,
                "scene_registry.json");

            new SceneRegistryPersistence()
                .Save(SceneRegistry, sceneFile);
        }
        catch (Exception ex)
        {
            _log.Log(
                FtpLogLevel.Warn,
                $"Scene registry persistence failed during shutdown: {ex.Message}",
                ex);
        }

        try
        {
            var releaseFile = _releaseRegistryPath ?? Path.Combine(
                Path.GetDirectoryName(Runtime.ConfigFilePath)!,
                "release_registry.json");

            new ReleaseRegistryPersistence()
                .Save(Runtime.ReleaseRegistry, releaseFile);
        }
        catch (Exception ex)
        {
            _log.Log(
                FtpLogLevel.Warn,
                $"Release registry persistence failed during shutdown: {ex.Message}",
                ex);
        }
        _cts?.Cancel();
        _listener?.Stop();

        if (_irc is not null)
        {
            try
            {
                _irc.DisposeAsync().AsTask().Wait();
            }
            catch { }
        }

        if (_housekeeping is not null)
        {
            _housekeeping.DisposeAsync().AsTask().Wait();
            _housekeeping = null;
        }


        // Dispose AMScript engines
        ScriptEngines? scripts;
        lock (_configLock)
        {
            scripts = _scripts;
            _scripts = null;
        }
        scripts?.Dispose();

        // 🔑 Always release DB lock if present
        Runtime.Database?.Dispose();

        // Dispose dupe store backend if it holds file handles (BinaryDupeStore)
        if (Runtime.DupeStore is IDisposable dispDupe)
        {
            try { dispDupe.Dispose(); }
            catch { }
        }



        // Stop monitoring endpoints
        lock (_monitoringLock)
        {
            if (StatusEndpoint is not null)
            {
                try { StatusEndpoint.DisposeAsync().AsTask().Wait(); } catch { }
                StatusEndpoint = null;
            }

            if (MetricsEndpoint is not null)
            {
                try { MetricsEndpoint.DisposeAsync().AsTask().Wait(); } catch { }
                MetricsEndpoint = null;
            }
        }

        _log.Log(FtpLogLevel.Info, "Server stopped.");
    }
}