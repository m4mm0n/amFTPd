/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RestApiRouter.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24 00:00:00
 *  Last Modified:  2026-04-24 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      JSON REST API router, served on /api/* by the StatusEndpoint's HttpListener.
 *      All endpoints require the same auth token as the status/metrics endpoints.
 *
 *      Read endpoints (GET):
 *          /api/sessions                   — active FTP sessions
 *          /api/users                      — all users
 *          /api/users/{name}               — single user by name
 *          /api/stats                      — aggregate server stats
 *          /api/stats/sections             — per-section live stats
 *          /api/stats/top                  — top uploaders (query: window, count, section)
 *          /api/dupes/search               — dupe search (query: q, limit)
 *          /api/pres                       — recent pres (query: count, section)
 *
 *      Write endpoints (POST, JSON body):
 *          /api/kick       {user}                          — kick all sessions of user
 *          /api/ban        {ip, reason?, durationMinutes?} — ban an IP
 *          /api/pre        {section, releaseName}          — register a pre
 *          /api/rehash     {}                              — reload config
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Pre;
using amFTPd.Core.Stats;
using amFTPd.Logging;

namespace amFTPd.Core.Api;

/// <summary>
/// Routes /api/* HTTP requests to REST handler methods.
/// </summary>
public sealed class RestApiRouter
{
    private readonly AmFtpdRuntimeConfig _runtime;
    private readonly IFtpLogger _log;

    // Optional — needed for write operations that touch server state.
    // Set by FtpServer after the StatusEndpoint is created.
    internal FtpServer? Server { get; set; }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RestApiRouter(AmFtpdRuntimeConfig runtime, IFtpLogger log)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    // ------------------------------------------------------------------
    // Entry point called by StatusEndpoint
    // ------------------------------------------------------------------

    public async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var path = (req.Url?.AbsolutePath ?? "/api").TrimEnd('/');

        try
        {
            var method = req.HttpMethod.ToUpperInvariant();

            // Normalise: strip /api prefix for matching
            if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                await WriteError(res, 404, "Not found.");
                return;
            }

            var route = path["/api".Length..].ToLowerInvariant(); // e.g. "" | "/sessions" | "/users/bob"

            if (method == "GET")
            {
                if (route == "" || route == "/") await GetRoot(res);
                else if (route == "/sessions") await GetSessions(res);
                else if (route == "/users") await GetUsers(res);
                else if (route.StartsWith("/users/")) await GetUser(res, route["/users/".Length..]);
                else if (route == "/stats") await GetStats(res);
                else if (route == "/stats/sections") await GetStatsSections(res);
                else if (route == "/stats/top") await GetStatsTop(req, res);
                else if (route == "/dupes/search") await GetDupesSearch(req, res);
                else if (route == "/pres") await GetPres(req, res);
                else await WriteError(res, 404, "Unknown endpoint.");
            }
            else if (method == "POST")
            {
                var body = await ReadBody(req);

                if (route == "/kick") await PostKick(res, body);
                else if (route == "/ban") await PostBan(res, body);
                else if (route == "/pre") await PostPre(res, body);
                else if (route == "/rehash") await PostRehash(res);
                else await WriteError(res, 404, "Unknown endpoint.");
            }
            else
            {
                await WriteError(res, 405, "Method not allowed.");
            }
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Debug, $"[API] Request error: {ex.Message}", ex);
            try { await WriteError(res, 500, "Internal server error."); } catch { }
        }
    }

    // ------------------------------------------------------------------
    // GET /api/
    // ------------------------------------------------------------------

    private async Task GetRoot(HttpListenerResponse res)
    {
        await WriteJson(res, new
        {
            endpoints = new[]
            {
                "GET  /api/sessions",
                "GET  /api/users",
                "GET  /api/users/{name}",
                "GET  /api/stats",
                "GET  /api/stats/sections",
                "GET  /api/stats/top?window=all|today|week|month&count=N&section=S",
                "GET  /api/dupes/search?q=term&limit=N",
                "GET  /api/pres?count=N&section=S",
                "POST /api/kick        {\"user\":\"name\"}",
                "POST /api/ban         {\"ip\":\"1.2.3.4\",\"reason\":\"...\",\"durationMinutes\":N}",
                "POST /api/pre         {\"section\":\"0DAY\",\"releaseName\":\"Release-GRP\"}",
                "POST /api/rehash      {}"
            }
        });
    }

    // ------------------------------------------------------------------
    // GET /api/sessions
    // ------------------------------------------------------------------

    private async Task GetSessions(HttpListenerResponse res)
    {
        var sessions = _runtime.EventBus.GetActiveSessions();

        var payload = sessions.Select(s => new
        {
            sessionId = s.SessionId,
            user = s.Account?.UserName,
            group = s.Account?.GroupName,
            remoteIp = s.RemoteEndPoint?.Address?.ToString(),
            transferDir = s.TransferDirection == 0 ? "idle" :
                            s.TransferDirection == 1 ? "upload" : "download",
            transferFile = s.TransferFileName,
            bytesCompleted = Interlocked.Read(ref s.TransferBytesCompleted),
            bytesTotal = Volatile.Read(ref s.TransferTotalBytes)
        }).ToList();

        await WriteJson(res, new { count = payload.Count, sessions = payload });
    }

    // ------------------------------------------------------------------
    // GET /api/users
    // ------------------------------------------------------------------

    private async Task GetUsers(HttpListenerResponse res)
    {
        var users = _runtime.UserStore.GetAllUsers().Select(MapUser).ToList();
        await WriteJson(res, new { count = users.Count, users });
    }

    // ------------------------------------------------------------------
    // GET /api/users/{name}
    // ------------------------------------------------------------------

    private async Task GetUser(HttpListenerResponse res, string name)
    {
        var user = _runtime.UserStore.FindUser(name);
        if (user is null)
        {
            await WriteError(res, 404, $"User not found: {name}");
            return;
        }
        await WriteJson(res, MapUser(user));
    }

    // ------------------------------------------------------------------
    // GET /api/stats
    // ------------------------------------------------------------------

    private async Task GetStats(HttpListenerResponse res)
    {
        var perf = PerfCounters.GetSnapshot();
        var live = _runtime.LiveStats;
        var sessions = _runtime.EventBus.GetActiveSessions().Count;

        await WriteJson(res, new
        {
            nowUtc = DateTimeOffset.UtcNow,
            activeSessions = sessions,
            activeTransfers = perf.ActiveTransfers,
            totalTransfers = perf.TotalTransfers,
            bytesUploaded = perf.BytesUploaded,
            bytesDownloaded = perf.BytesDownloaded,
            failedLogins = perf.FailedLogins,
            totalCommands = perf.TotalCommands,
            abortedTransfers = perf.AbortedTransfers,
            totalConnections = perf.TotalConnections,
            maxConcurrentTransfers = perf.MaxConcurrentTransfers,
            nukes = perf.TotalNukes,
            unnukes = perf.TotalUnnukes,
            pres = perf.TotalPres,
            rolling = new
            {
                transfersPerSecond = new
                {
                    s5 = _runtime.RollingStats.Transfers5s.RatePerSecond(),
                    m1 = _runtime.RollingStats.Transfers1m.RatePerSecond(),
                    m5 = _runtime.RollingStats.Transfers5m.RatePerSecond()
                }
            }
        });
    }

    // ------------------------------------------------------------------
    // GET /api/stats/sections
    // ------------------------------------------------------------------

    private async Task GetStatsSections(HttpListenerResponse res)
    {
        var sections = _runtime.LiveStats.Sections.Values.Select(s => new
        {
            section = s.SectionName,
            activeUsers = s.ActiveUsers,
            uploads = s.Uploads,
            downloads = s.Downloads,
            bytesUploaded = s.BytesUploaded,
            bytesDownloaded = s.BytesDownloaded
        }).OrderBy(s => s.section).ToList();

        await WriteJson(res, new { count = sections.Count, sections });
    }

    // ------------------------------------------------------------------
    // GET /api/stats/top
    // ------------------------------------------------------------------

    private async Task GetStatsTop(HttpListenerRequest req, HttpListenerResponse res)
    {
        var windowStr = req.QueryString["window"] ?? "all";
        var countStr = req.QueryString["count"] ?? "10";
        var section = req.QueryString["section"];

        var window = windowStr.ToLowerInvariant() switch
        {
            "today" => LeaderboardWindow.Today,
            "week" => LeaderboardWindow.ThisWeek,
            "month" => LeaderboardWindow.ThisMonth,
            _ => LeaderboardWindow.AllTime
        };

        if (!int.TryParse(countStr, out var count) || count < 1) count = 10;
        if (count > 100) count = 100;

        try
        {
            var logPath = SessionLogStatsService.GetDefaultLogPath(_runtime);
            var entries = LeaderboardService.TopUploaders(logPath, window, count, section);
            var payload = entries.Select((e, i) => new
            {
                rank = i + 1,
                user = e.Name,
                group = e.Group,
                files = e.Files,
                bytesUploaded = e.BytesUploaded
            }).ToList();

            await WriteJson(res, new { window = windowStr, count = payload.Count, section, top = payload });
        }
        catch (Exception ex)
        {
            await WriteError(res, 500, $"Failed to compute leaderboard: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // GET /api/dupes/search?q=...&limit=N
    // ------------------------------------------------------------------

    private async Task GetDupesSearch(HttpListenerRequest req, HttpListenerResponse res)
    {
        var q = req.QueryString["q"]?.Trim() ?? "";
        var limitStr = req.QueryString["limit"] ?? "20";

        if (string.IsNullOrWhiteSpace(q))
        {
            await WriteError(res, 400, "Missing query parameter: q");
            return;
        }

        if (!int.TryParse(limitStr, out var limit) || limit < 1) limit = 20;
        if (limit > 200) limit = 200;

        var dupeStore = _runtime.DupeStore;
        if (dupeStore is null)
        {
            await WriteError(res, 503, "Dupe store not enabled.");
            return;
        }

        var results = dupeStore
            .Search(q, limit: limit)
            .Select(d => new
            {
                releaseName = d.ReleaseName,
                section = d.SectionName,
                virtualPath = d.VirtualPath,
                totalBytes = d.TotalBytes,
                firstSeen = d.FirstSeen,
                lastUpdated = d.LastUpdated,
                uploaderUser = d.UploaderUser,
                uploaderGroup = d.UploaderGroup,
                isNuked = d.IsNuked,
                nukeReason = d.NukeReason
            }).ToList();

        await WriteJson(res, new { query = q, count = results.Count, results });
    }

    // ------------------------------------------------------------------
    // GET /api/pres?count=N&section=S
    // ------------------------------------------------------------------

    private async Task GetPres(HttpListenerRequest req, HttpListenerResponse res)
    {
        var countStr = req.QueryString["count"] ?? "20";
        var section = req.QueryString["section"];

        if (!int.TryParse(countStr, out var count) || count < 1) count = 20;
        if (count > 200) count = 200;

        IEnumerable<PreEntry> pres = _runtime.PreRegistry.All;

        if (!string.IsNullOrWhiteSpace(section))
            pres = pres.Where(p => p.Section.Equals(section, StringComparison.OrdinalIgnoreCase));

        var results = pres.Take(count).Select(p => new
        {
            releaseName = p.ReleaseName,
            section = p.Section,
            user = p.User,
            group = p.Group,
            timestamp = p.Timestamp,
            fileCount = p.FileCount,
            totalBytes = p.TotalBytes,
            tags = p.Tags,
            status = p.Status.ToString().ToLower()
        }).ToList();

        await WriteJson(res, new { count = results.Count, pres = results });
    }

    // ------------------------------------------------------------------
    // POST /api/kick   {"user":"name"}
    // ------------------------------------------------------------------

    private async Task PostKick(HttpListenerResponse res, JsonDocument? body)
    {
        var userName = body?.RootElement.TryGetProperty("user", out var u) == true
            ? u.GetString()?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(userName))
        {
            await WriteError(res, 400, "Missing field: user");
            return;
        }

        var sessions = _runtime.EventBus.GetActiveSessions()
            .Where(s => s.Account?.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (sessions.Count == 0)
        {
            await WriteJson(res, new { kicked = 0, message = $"No active sessions for {userName}." });
            return;
        }

        foreach (var sess in sessions)
        {
            try
            {
                sess.MarkQuit();
                try { sess.Control.Close(); } catch { }
            }
            catch { }
        }

        _runtime.AuditLog?.Log("api", "KICK", userName, $"sessions={sessions.Count}", null);

        await WriteJson(res, new { kicked = sessions.Count, user = userName });
    }

    // ------------------------------------------------------------------
    // POST /api/ban   {"ip":"1.2.3.4","reason":"...","durationMinutes":N}
    // ------------------------------------------------------------------

    private async Task PostBan(HttpListenerResponse res, JsonDocument? body)
    {
        if (Server is null)
        {
            await WriteError(res, 503, "Server reference not available for ban operations.");
            return;
        }

        var ipStr = body?.RootElement.TryGetProperty("ip", out var ip) == true
            ? ip.GetString()?.Trim() : null;

        if (string.IsNullOrWhiteSpace(ipStr) || !System.Net.IPAddress.TryParse(ipStr, out var address))
        {
            await WriteError(res, 400, "Missing or invalid field: ip");
            return;
        }

        var reason = body?.RootElement.TryGetProperty("reason", out var r) == true
            ? r.GetString()?.Trim() : null;

        int? durationMinutes = null;
        if (body?.RootElement.TryGetProperty("durationMinutes", out var dm) == true
            && dm.TryGetInt32(out var dmVal))
            durationMinutes = dmVal;

        if (durationMinutes.HasValue && durationMinutes.Value > 0)
            Server.BanList.AddTemporaryBan(address, TimeSpan.FromMinutes(durationMinutes.Value), reason);
        else
            Server.BanList.AddPermanentBan(address, reason);

        _runtime.AuditLog?.Log("api", "BAN", ipStr,
            $"reason={reason ?? "(none)"} duration={durationMinutes?.ToString() ?? "permanent"}min", null);

        await WriteJson(res, new
        {
            banned = ipStr,
            reason,
            durationMinutes = durationMinutes ?? 0,
            permanent = !durationMinutes.HasValue || durationMinutes.Value <= 0
        });
    }

    // ------------------------------------------------------------------
    // POST /api/pre   {"section":"0DAY","releaseName":"Release-GRP"}
    // ------------------------------------------------------------------

    private async Task PostPre(HttpListenerResponse res, JsonDocument? body)
    {
        var section = body?.RootElement.TryGetProperty("section", out var s) == true
            ? s.GetString()?.Trim() : null;

        var releaseName = body?.RootElement.TryGetProperty("releaseName", out var r) == true
            ? r.GetString()?.Trim() : null;

        if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(releaseName))
        {
            await WriteError(res, 400, "Missing fields: section, releaseName");
            return;
        }

        var virtPath = $"/{section}/{releaseName}".Replace('\\', '/');
        var now = DateTimeOffset.UtcNow;

        var entry = new PreEntry(section, releaseName, virtPath, "api", now)
        {
            Status = PreStatus.Approved
        };

        _runtime.PreRegistry.AddOrReplace(entry);

        var dupeStore = _runtime.DupeStore;
        if (dupeStore is not null)
        {
            var existing = dupeStore.Find(section, releaseName);
            var dupeEntry = (existing ?? new DupeEntry()) with
            {
                ReleaseName = releaseName,
                SectionName = section,
                VirtualPath = virtPath,
                FirstSeen = now,
                LastUpdated = now,
                UploaderUser = "api"
            };
            dupeStore.Upsert(dupeEntry);
        }

        _runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.Pre,
            Timestamp = now,
            User = "api",
            Section = section,
            VirtualPath = virtPath,
            ReleaseName = releaseName
        });

        _runtime.AuditLog?.Log("api", "PRE", releaseName, $"section={section}", null);

        await WriteJson(res, new { registered = releaseName, section, virtualPath = virtPath });
    }

    // ------------------------------------------------------------------
    // POST /api/rehash
    // ------------------------------------------------------------------

    private async Task PostRehash(HttpListenerResponse res)
    {
        if (Server is null)
        {
            await WriteError(res, 503, "Server reference not available for rehash.");
            return;
        }

        try
        {
            var (success, message, _) = await Server.ReloadConfigurationAsync().ConfigureAwait(false);

            _runtime.AuditLog?.Log("api", "REHASH", null,
                success ? "ok" : $"failed: {message}", null);

            if (success)
                await WriteJson(res, new { success = true, message });
            else
            {
                res.StatusCode = 500;
                await WriteJson(res, new { success = false, message });
            }
        }
        catch (Exception ex)
        {
            await WriteError(res, 500, $"Rehash failed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static object MapUser(FtpUser u) => new
    {
        userName = u.UserName,
        group = u.GroupName,
        secondaryGroups = u.SecondaryGroups,
        isAdmin = u.IsAdmin,
        isSiteop = u.IsSiteop,
        disabled = u.Disabled,
        isNoRatio = u.IsNoRatio,
        maxConcurrentLogins = u.MaxConcurrentLogins,
        creditsKb = u.CreditsKb,
        maxUploadKbps = u.MaxUploadKbps,
        maxDownloadKbps = u.MaxDownloadKbps,
        allowFxp = u.AllowFxp,
        allowUpload = u.AllowUpload,
        allowDownload = u.AllowDownload
    };

    private static async Task<JsonDocument?> ReadBody(HttpListenerRequest req)
    {
        try
        {
            using var sr = new System.IO.StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            var body = await sr.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
                return null;
            return JsonDocument.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteJson(HttpListenerResponse res, object payload, int statusCode = 200)
    {
        res.StatusCode = statusCode;
        res.ContentType = "application/json; charset=utf-8";

        var json = JsonSerializer.Serialize(payload, _jsonOpts);
        var data = Encoding.UTF8.GetBytes(json);

        res.ContentLength64 = data.Length;
        await res.OutputStream.WriteAsync(data).ConfigureAwait(false);
    }

    private static async Task WriteError(HttpListenerResponse res, int statusCode, string message)
        => await WriteJson(res, new { error = message }, statusCode);
}
