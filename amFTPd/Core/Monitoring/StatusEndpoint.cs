/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           StatusEndpoint.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 16:58:26
 *  Last Modified:  2025-12-14 22:07:09
 *  CRC32:          0x63D2B33C
 *  
 *  Description:
 *      Minimal HTTP status endpoint based on <see cref="HttpListener"/>. Intended for local monitoring / health checks.
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
using amFTPd.Core.Stats;
using amFTPd.Logging;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace amFTPd.Core.Monitoring
{
    /// <summary>
    /// Minimal HTTP status endpoint based on <see cref="HttpListener"/>.
    /// Intended for local monitoring / health checks.
    /// </summary>
    public sealed class StatusEndpoint : IAsyncDisposable
    {
        private readonly AmFtpdRuntimeConfig _runtime;
        private readonly IFtpLogger _log;
        private readonly AmFtpdStatusConfig _cfg;
        private readonly string _prefix;

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        public StatusEndpoint(
            AmFtpdRuntimeConfig runtime,
            IFtpLogger log,
            AmFtpdStatusConfig cfg)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));

            // Normalize path
            var path = string.IsNullOrWhiteSpace(cfg.Path) ? "/amftpd-status/" : cfg.Path.Trim();
            if (!path.StartsWith("/"))
                path = "/" + path;
            if (!path.EndsWith("/"))
                path += "/";

            _prefix = $"http://{cfg.BindAddress}:{cfg.Port}{path}";
        }

        /// <summary>
        /// Starts the HTTP status endpoint on a background task.
        /// </summary>
        public void Start()
        {
            if (!_cfg.Enabled)
            {
                _log.Log(FtpLogLevel.Info, "[Status] HTTP endpoint disabled by configuration.");
                return;
            }

            if (!HttpListener.IsSupported)
            {
                _log.Log(FtpLogLevel.Warn,
                    "[Status] HttpListener is not supported on this platform; HTTP status endpoint disabled.");
                return;
            }

            if (_listener is not null)
                return; // already started

            _listener = new HttpListener();
            _listener.Prefixes.Add(_prefix);

            _cts = new CancellationTokenSource();

            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn,
                    $"[Status] Failed to start HTTP listener on '{_prefix}': {ex.Message}", ex);
                _listener = null;
                _cts.Cancel();
                return;
            }

            _log.Log(FtpLogLevel.Info, $"[Status] HTTP endpoint listening at {_prefix}");

            _loopTask = Task.Run(() => RunAsync(_cts.Token));
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            if (_listener is null)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? ctx = null;

                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.Log(FtpLogLevel.Debug, $"[Status] Listener error: {ex.Message}", ex);
                    continue;
                }

                if (ctx is null)
                    continue;

                _ = Task.Run(() => HandleRequestAsync(ctx), cancellationToken);
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var res = ctx.Response;

                var path = req.Url?.AbsolutePath ?? "/";
                if (path.EndsWith("/"))
                    path = path[..^1];

                // Accept /status or the configured path, with or without trailing slash
                var configPath = _cfg.Path;
                if (string.IsNullOrWhiteSpace(configPath))
                    configPath = "/amftpd-status/";
                if (configPath.EndsWith("/"))
                    configPath = configPath[..^1];

                var isStatusPath =
                    string.Equals(path, "/status", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, configPath, StringComparison.OrdinalIgnoreCase);

                if (isStatusPath)
                {
                    await WriteStatusAsync(res).ConfigureAwait(false);
                }
                else
                {
                    res.StatusCode = (int)HttpStatusCode.NotFound;
                    res.ContentType = "application/json";
                    var notFound = "{\"status\":\"not-found\"}";
                    var data = Encoding.UTF8.GetBytes(notFound);
                    await res.OutputStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Debug, $"[Status] Request handling error: {ex.Message}", ex);
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { /* ignore */ }
            }
        }

        private async Task WriteStatusAsync(HttpListenerResponse res)
        {
            var now = DateTimeOffset.UtcNow;
            var ftpCfg = _runtime.FtpConfig;

            var perf = PerfCounters.GetSnapshot();
            var activeSessions = _runtime.EventBus.GetActiveSessions().Count;

            // Best-effort daily stats from session log
            object? daily = null;
            try
            {
                var from = now.AddDays(-1);
                var logPath = SessionLogStatsService.GetDefaultLogPath(_runtime);
                var stats = SessionLogStatsService.Compute(logPath, from, now);

                daily = new
                {
                    fromUtc = stats.FromUtc,
                    toUtc = stats.ToUtc,
                    uploads = stats.Uploads,
                    downloads = stats.Downloads,
                    bytesUploaded = stats.BytesUploaded,
                    bytesDownloaded = stats.BytesDownloaded,
                    nukes = stats.Nukes,
                    pres = stats.Pres,
                    logins = stats.Logins,
                    users = stats.UniqueUsers
                };
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Debug, $"[Status] Failed to compute daily stats: {ex.Message}", ex);
                _log.Log(FtpLogLevel.Debug, $"[Status] Failed to compute daily stats: {ex.Message}", ex);
            }

            var assembly = Assembly.GetExecutingAssembly();
            var ver = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";

            var payload = new
            {
                status = "ok",
                nowUtc = now,
                version = ver,
                server = new
                {
                    bindAddress = ftpCfg.BindAddress,
                    port = ftpCfg.Port,
                    root = ftpCfg.RootPath
                },
                sessions = new
                {
                    active = activeSessions
                },
                transfers = new
                {
                    bytesUploaded = perf.BytesUploaded,
                    bytesDownloaded = perf.BytesDownloaded,
                    activeTransfers = perf.ActiveTransfers,
                    totalTransfers = perf.TotalTransfers,
                    averageMsPerTransfer = perf.AverageTransferMilliseconds,
                    maxConcurrentTransfers = perf.MaxConcurrentTransfers
                },
                daily
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            var data = Encoding.UTF8.GetBytes(json);
            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentType = "application/json";
            res.ContentEncoding = Encoding.UTF8;
            res.ContentLength64 = data.Length;

            await res.OutputStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts?.CancelAsync();

            if (_loopTask is not null)
            {
                try { await _loopTask.ConfigureAwait(false); }
                catch { /* ignore */ }
            }

            if (_listener is not null)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch
                {
                    // ignored
                }
            }
        }

        public void Stop()
        {
            _ = DisposeAsync();
        }
    }
}
