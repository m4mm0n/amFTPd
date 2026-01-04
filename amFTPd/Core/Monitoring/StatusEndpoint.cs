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

namespace amFTPd.Core.Monitoring;

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
    private readonly string _ipSalt = Guid.NewGuid().ToString("N");

    private const int MaxIpEntries = 10;
    private const int DefaultMaxIpEntries = 10;
    private const int AbsoluteMaxIpEntries = 50;

    /// <summary>
    /// Initializes a new instance of the StatusEndpoint class with the specified runtime configuration, logger, and
    /// status endpoint settings.
    /// </summary>
    /// <param name="runtime">The runtime configuration used to access application state and services. Cannot be null.</param>
    /// <param name="log">The logger used to record FTP server status and diagnostic information. Cannot be null.</param>
    /// <param name="cfg">The configuration settings for the status endpoint, including network binding and path information. Cannot
    /// be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if runtime, log, or cfg is null.</exception>

    private static string NormalizeHost(string? bindAddress)
    {
        // HttpListener does NOT accept 0.0.0.0 / :: in prefixes.
        // Use '+' for all interfaces, or 'localhost' if empty.
        if (string.IsNullOrWhiteSpace(bindAddress))
            return "localhost";

        var v = bindAddress.Trim();

        if (v.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) || v.Equals("::", StringComparison.OrdinalIgnoreCase))
            return "+";

        return v;
    }

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

        var host = NormalizeHost(cfg.BindAddress);

        _prefix = $"http://{host}:{cfg.Port}{path}";
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

            // Optional token auth (AuthToken)
            if (!IsAuthorized(req))
            {
                res.StatusCode = (int)HttpStatusCode.Unauthorized;
                res.ContentType = "application/json";
                var payload = Encoding.UTF8.GetBytes("{\"status\":\"unauthorized\"}");
                await res.OutputStream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                return;
            }

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

            if (path.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            {
                await WriteMetricsAsync(res).ConfigureAwait(false);
                return;
            }

            if (isStatusPath)
            {
                await WriteStatusAsync(req, res).ConfigureAwait(false);
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
    private async Task WriteMetricsAsync(HttpListenerResponse res)
    {
        var perf = PerfCounters.GetSnapshot();
        var live = _runtime.LiveStats;

        var sb = new StringBuilder();

        // --- core server metrics ---------------------------------------------
        sb.AppendLine("# HELP amftpd_active_connections Current active control connections");
        sb.AppendLine("# TYPE amftpd_active_connections gauge");
        sb.AppendLine($"amftpd_active_connections {perf.ActiveConnections}");

        sb.AppendLine("# HELP amftpd_total_commands Total FTP commands executed");
        sb.AppendLine("# TYPE amftpd_total_commands counter");
        sb.AppendLine($"amftpd_total_commands {perf.TotalCommands}");

        sb.AppendLine("# HELP amftpd_failed_logins Total failed login attempts");
        sb.AppendLine("# TYPE amftpd_failed_logins counter");
        sb.AppendLine($"amftpd_failed_logins {perf.FailedLogins}");

        sb.AppendLine("# HELP amftpd_aborted_transfers Total aborted transfers");
        sb.AppendLine("# TYPE amftpd_aborted_transfers counter");
        sb.AppendLine($"amftpd_aborted_transfers {perf.AbortedTransfers}");

        // --- transfer metrics -------------------------------------------------
        sb.AppendLine("# HELP amftpd_bytes_uploaded_total Total uploaded bytes");
        sb.AppendLine("# TYPE amftpd_bytes_uploaded_total counter");
        sb.AppendLine($"amftpd_bytes_uploaded_total {perf.BytesUploaded}");

        sb.AppendLine("# HELP amftpd_bytes_downloaded_total Total downloaded bytes");
        sb.AppendLine("# TYPE amftpd_bytes_downloaded_total counter");
        sb.AppendLine($"amftpd_bytes_downloaded_total {perf.BytesDownloaded}");

        sb.AppendLine("# HELP amftpd_active_transfers Current active data transfers");
        sb.AppendLine("# TYPE amftpd_active_transfers gauge");
        sb.AppendLine($"amftpd_active_transfers {perf.ActiveTransfers}");

        sb.AppendLine("# HELP amftpd_max_concurrent_transfers Peak concurrent transfers");
        sb.AppendLine("# TYPE amftpd_max_concurrent_transfers gauge");
        sb.AppendLine($"amftpd_max_concurrent_transfers {perf.MaxConcurrentTransfers}");

        // --- per-user live stats ---------------------------------------------
        sb.AppendLine("# HELP amftpd_user_uploads_total Upload count per user");
        sb.AppendLine("# TYPE amftpd_user_uploads_total counter");

        sb.AppendLine("# HELP amftpd_user_downloads_total Download count per user");
        sb.AppendLine("# TYPE amftpd_user_downloads_total counter");

        sb.AppendLine("# HELP amftpd_user_bytes_uploaded_total Uploaded bytes per user");
        sb.AppendLine("# TYPE amftpd_user_bytes_uploaded_total counter");

        sb.AppendLine("# HELP amftpd_user_bytes_downloaded_total Downloaded bytes per user");
        sb.AppendLine("# TYPE amftpd_user_bytes_downloaded_total counter");

        sb.AppendLine("# HELP amftpd_user_active_sessions Active sessions per user");
        sb.AppendLine("# TYPE amftpd_user_active_sessions gauge");

        foreach (var u in live.Users.Values)
        {
            var name = EscapeLabel(u.UserName);

            sb.AppendLine($"amftpd_user_uploads_total{{user=\"{name}\"}} {u.Uploads}");
            sb.AppendLine($"amftpd_user_downloads_total{{user=\"{name}\"}} {u.Downloads}");
            sb.AppendLine($"amftpd_user_bytes_uploaded_total{{user=\"{name}\"}} {u.BytesUploaded}");
            sb.AppendLine($"amftpd_user_bytes_downloaded_total{{user=\"{name}\"}} {u.BytesDownloaded}");
            sb.AppendLine($"amftpd_user_active_sessions{{user=\"{name}\"}} {u.ActiveSessions}");
        }

        // --- per-section live stats ------------------------------------------
        sb.AppendLine("# HELP amftpd_section_uploads_total Upload count per section");
        sb.AppendLine("# TYPE amftpd_section_uploads_total counter");

        sb.AppendLine("# HELP amftpd_section_downloads_total Download count per section");
        sb.AppendLine("# TYPE amftpd_section_downloads_total counter");

        sb.AppendLine("# HELP amftpd_section_bytes_uploaded_total Uploaded bytes per section");
        sb.AppendLine("# TYPE amftpd_section_bytes_uploaded_total counter");

        sb.AppendLine("# HELP amftpd_section_bytes_downloaded_total Downloaded bytes per section");
        sb.AppendLine("# TYPE amftpd_section_bytes_downloaded_total counter");

        sb.AppendLine("# HELP amftpd_section_active_users Active users per section");
        sb.AppendLine("# TYPE amftpd_section_active_users gauge");

        foreach (var s in live.Sections.Values)
        {
            var name = EscapeLabel(s.SectionName);

            sb.AppendLine($"amftpd_section_uploads_total{{section=\"{name}\"}} {s.Uploads}");
            sb.AppendLine($"amftpd_section_downloads_total{{section=\"{name}\"}} {s.Downloads}");
            sb.AppendLine($"amftpd_section_bytes_uploaded_total{{section=\"{name}\"}} {s.BytesUploaded}");
            sb.AppendLine($"amftpd_section_bytes_downloaded_total{{section=\"{name}\"}} {s.BytesDownloaded}");
            sb.AppendLine($"amftpd_section_active_users{{section=\"{name}\"}} {s.ActiveUsers}");
        }

        sb.AppendLine("# HELP amftpd_ip_active_sessions Active sessions per IP (anonymized)");
        sb.AppendLine("# TYPE amftpd_ip_active_sessions gauge");

        // Bound IP label cardinality (top N + _other)
        var maxIps = Math.Clamp(_cfg.MaxIpEntries, 1, AbsoluteMaxIpEntries);
        var orderedIps = live.Ips
            .OrderByDescending(kv => kv.Value.BytesUploaded + kv.Value.BytesDownloaded)
            .ToList();

        foreach (var kv in orderedIps.Take(maxIps))
        {
            var anon = AnonymizeIp(kv.Key);
            var s = kv.Value;

            sb.AppendLine($"amftpd_ip_active_sessions{{ip=\"{anon}\"}} {s.ActiveSessions}");
        }

        if (orderedIps.Count > maxIps)
        {
            var rest = orderedIps.Skip(maxIps);
            sb.AppendLine($"amftpd_ip_active_sessions{{ip=\"_other\"}} {rest.Sum(i => i.Value.ActiveSessions)}");
        }


        var data = Encoding.UTF8.GetBytes(sb.ToString());

        res.StatusCode = 200;
        res.ContentType = "text/plain; version=0.0.4";
        res.ContentEncoding = Encoding.UTF8;
        res.ContentLength64 = data.Length;

        await res.OutputStream.WriteAsync(data, 0, data.Length);
    }
    private async Task WriteStatusAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        var now = DateTimeOffset.UtcNow;
        var ftpCfg = _runtime.FtpConfig;

        var perf = PerfCounters.GetSnapshot();

        // Optional rate-aware stats
        StatsSnapshot? rate = null;
        try
        {
            rate = _runtime.StatsCollector?.GetSnapshot();
        }
        catch
        {
            // best effort only
        }

        var activeSessions = _runtime.EventBus.GetActiveSessions().Count;

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
        }

        var assembly = Assembly.GetExecutingAssembly();
        var ver =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

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

            ips = BuildIpStatsForStatus(req),

            rates = rate is null ? null : new
            {
                commandsPerSecond = rate.CommandsPerSecond,
                averageTransferDurationMs = rate.AverageTransferDurationMs
            },

            daily,

            rolling = new
            {
                transfersPerSecond = new
                {
                    s5 = _runtime.RollingStats.Transfers5s.RatePerSecond(),
                    m1 = _runtime.RollingStats.Transfers1m.RatePerSecond(),
                    m5 = _runtime.RollingStats.Transfers5m.RatePerSecond()
                }
            }
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

    /// <summary>
    /// Asynchronously releases all resources used by the current instance.
    /// </summary>
    /// <remarks>Call this method to clean up resources when the instance is no longer needed. This
    /// method should be awaited to ensure that all resources are released before continuing execution.</remarks>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
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
    /// <summary>
    /// Stops the current operation and releases any associated resources.
    /// </summary>
    public void Stop() => _ = DisposeAsync();


    private bool IsAuthorized(HttpListenerRequest req)
    {
        var token = _cfg.AuthToken;
        if (string.IsNullOrWhiteSpace(token))
            return true;

        // 1) Authorization: Bearer <token>
        var auth = req.Headers["Authorization"];
        if (!string.IsNullOrWhiteSpace(auth))
        {
            const string prefix = "Bearer ";
            if (auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var v = auth.Substring(prefix.Length).Trim();
                if (string.Equals(v, token, StringComparison.Ordinal))
                    return true;
            }
        }

        // 2) X-AmFTPd-Token: <token>
        var header = req.Headers["X-AmFTPd-Token"];
        if (!string.IsNullOrWhiteSpace(header) && string.Equals(header.Trim(), token, StringComparison.Ordinal))
            return true;

        // 3) ?token=<token>
        var q = req.QueryString["token"];
        if (!string.IsNullOrWhiteSpace(q) && string.Equals(q.Trim(), token, StringComparison.Ordinal))
            return true;

        return false;
    }

    private IDictionary<string, object>? BuildIpStatsForStatus(HttpListenerRequest req)
    {
        // Defaults controlled by config; can be overridden via query params:
        //   ?ips=true|false
        //   ?maxIps=N
        var include = _cfg.IncludeIpStatsByDefault;
        var ipsQ = req.QueryString["ips"];
        if (!string.IsNullOrWhiteSpace(ipsQ) && bool.TryParse(ipsQ, out var b))
            include = b;

        if (!include || _runtime.LiveStats.Ips.Count == 0)
            return null;

        var max = Math.Clamp(_cfg.MaxIpEntries, 1, AbsoluteMaxIpEntries);
        var maxQ = req.QueryString["maxIps"];
        if (!string.IsNullOrWhiteSpace(maxQ) && int.TryParse(maxQ, out var n))
            max = Math.Clamp(n, 1, AbsoluteMaxIpEntries);

        var ordered = _runtime.LiveStats.Ips
            .OrderByDescending(kv => kv.Value.BytesUploaded + kv.Value.BytesDownloaded)
            .ToList();

        var top = ordered.Take(max);
        var rest = ordered.Skip(max);

        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in top)
        {
            var s = kv.Value;
            dict["ip_" + AnonymizeIp(kv.Key)] = new
            {
                activeSessions = s.ActiveSessions,
                uploads = s.Uploads,
                downloads = s.Downloads,
                bytesUploaded = s.BytesUploaded,
                bytesDownloaded = s.BytesDownloaded
            };
        }

        if (rest.Any())
        {
            dict["_other"] = new
            {
                activeSessions = rest.Sum(i => i.Value.ActiveSessions),
                uploads = rest.Sum(i => i.Value.Uploads),
                downloads = rest.Sum(i => i.Value.Downloads),
                bytesUploaded = rest.Sum(i => i.Value.BytesUploaded),
                bytesDownloaded = rest.Sum(i => i.Value.BytesDownloaded)
            };
        }

        return dict;
    }

    private static string EscapeLabel(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    internal string AnonymizeIp(string ip)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(ip + _ipSalt);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
    internal StatusPayload BuildStatusPayload(
        bool includeIpStats,
        int? overrideMaxIps = null)
    {
        var now = DateTimeOffset.UtcNow;
        var perf = PerfCounters.GetSnapshot();
        var rt = _runtime;

        StatsSnapshot? rate = null;
        try { rate = rt.StatsCollector?.GetSnapshot(); } catch { }

        IDictionary<string, IpStatsPayload>? ips = null;

        if (includeIpStats && rt.LiveStats.Ips.Count > 0)
        {
            var maxIps = overrideMaxIps.HasValue
                ? Math.Clamp(overrideMaxIps.Value, 1, AbsoluteMaxIpEntries)
                : DefaultMaxIpEntries;

            var ordered = rt.LiveStats.Ips
                .OrderByDescending(kv =>
                    kv.Value.BytesUploaded + kv.Value.BytesDownloaded)
                .ToList();

            var top = ordered.Take(maxIps);
            var rest = ordered.Skip(maxIps);

            var dict = new Dictionary<string, IpStatsPayload>();

            foreach (var kv in top)
            {
                var s = kv.Value;
                dict["ip_" + AnonymizeIp(kv.Key)] = new IpStatsPayload
                {
                    ActiveSessions = s.ActiveSessions,
                    Uploads = s.Uploads,
                    Downloads = s.Downloads,
                    BytesUploaded = s.BytesUploaded,
                    BytesDownloaded = s.BytesDownloaded
                };
            }

            if (rest.Any())
            {
                dict["_other"] = new IpStatsPayload
                {
                    ActiveSessions = rest.Sum(i => i.Value.ActiveSessions),
                    Uploads = rest.Sum(i => i.Value.Uploads),
                    Downloads = rest.Sum(i => i.Value.Downloads),
                    BytesUploaded = rest.Sum(i => i.Value.BytesUploaded),
                    BytesDownloaded = rest.Sum(i => i.Value.BytesDownloaded)
                };
            }

            ips = dict;
        }

        return new StatusPayload
        {
            NowUtc = now,

            Sessions = new StatusSessionsPayload
            {
                Active = rt.EventBus.GetActiveSessions().Count
            },

            Transfers = new StatusTransfersPayload
            {
                BytesUploaded = perf.BytesUploaded,
                BytesDownloaded = perf.BytesDownloaded,
                ActiveTransfers = (int)perf.ActiveTransfers,
                TotalTransfers = perf.TotalTransfers,
                AverageTransferMilliseconds = perf.AverageTransferMilliseconds,
                MaxConcurrentTransfers = (int)perf.MaxConcurrentTransfers
            },

            Rolling = new StatusRollingPayload
            {
                TransfersPerSecond = new StatusRollingRatePayload
                {
                    S5 = rt.RollingStats.Transfers5s.RatePerSecond(),
                    M1 = rt.RollingStats.Transfers1m.RatePerSecond(),
                    M5 = rt.RollingStats.Transfers5m.RatePerSecond()
                }
            },

            Rates = rate is null
                ? null
                : new StatusRatesPayload
                {
                    CommandsPerSecond = rate.CommandsPerSecond,
                    AverageTransferDurationMs = rate.AverageTransferDurationMs
                },

            Ips = ips
        };
    }
}
