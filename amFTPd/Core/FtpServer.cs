/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-23
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

using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Config.Ident;
using amFTPd.Config.Scripting;
using amFTPd.Config.Vfs;
using amFTPd.Logging;
using amFTPd.Scripting;
using amFTPd.Security;
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
    private readonly FtpConfig _cfg;
    private readonly IUserStore _users;
    private readonly TlsConfig _tls;
    private readonly IFtpLogger _log;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly SectionManager _sections;

    private readonly IdentConfig _identCfg;
    private readonly VfsConfig _vfsCfg;

    private readonly AmFtpdRuntimeConfig _runtime;
    #endregion
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
        _log = log;

        // Assign all local fields from runtime
        _cfg = runtime.FtpConfig;
        _users = runtime.UserStore;
        _tls = runtime.TlsConfig;
        _sections = runtime.Sections;
        _identCfg = runtime.IdentConfig;
        _vfsCfg = runtime.VfsConfig;
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
        _listener = new TcpListener(new IPEndPoint(_cfg.BindAddress, _cfg.Port));
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

            _ = Task.Run(async () =>
            {
                var rem = client.Client.RemoteEndPoint?.ToString() ?? "??";
                _log.Log(FtpLogLevel.Info, $"Connection from {rem}");

                await using var session = new FtpSession(
                    client,
                    _log,
                    _cfg,
                    _users,
                    fs,
                    _cfg.DataChannelProtectionDefault,
                    _tls,
                    _identCfg,
                    _vfsCfg);

                var router = new FtpCommandRouter(session, _log, fs, _cfg, _tls, _sections, _runtime);

                // Attach script engines so router can use AMScript in credits/FXP/active
                router.AttachScriptEngines(
                    creditScript,
                    fxpScript,
                    activeScript,
                    sectionRoutingScript,
                    siteScript,
                    userScript, groupScript
                );
                
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
            });
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