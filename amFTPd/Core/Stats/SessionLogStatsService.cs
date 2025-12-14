/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SessionLogStatsService.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 16:58:06
 *  Last Modified:  2025-12-14 16:58:06
 *  CRC32:          0x4ED509E9
 *  
 *  Description:
 *      Helper for computing aggregated stats from the JSONL session log.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using System.Text;
using System.Text.Json;
using amFTPd.Config.Daemon;
using amFTPd.Core.Events;
using amFTPd.Db;

namespace amFTPd.Core.Stats;

/// <summary>
/// Helper for computing aggregated stats from the JSONL session log.
/// </summary>
public static class SessionLogStatsService
{
    private const string DefaultLogFileName = "amftpd-sessionlog.jsonl";

    /// <summary>
    /// Returns the default session log path based on <see cref="AmFtpdRuntimeConfig.ConfigFilePath"/>.
    /// </summary>
    public static string GetDefaultLogPath(AmFtpdRuntimeConfig runtime)
    {
        if (runtime is null) throw new ArgumentNullException(nameof(runtime));

        var configDir = Path.GetDirectoryName(runtime.ConfigFilePath);
        if (string.IsNullOrWhiteSpace(configDir))
            configDir = AppContext.BaseDirectory;

        return Path.Combine(configDir!, DefaultLogFileName);
    }

    /// <summary>
    /// Compute aggregated stats between <paramref name="fromUtc"/> and <paramref name="toUtc"/> (inclusive).
    /// </summary>
    public static AggregatedStats Compute(string logFilePath, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (fromUtc > toUtc)
            throw new ArgumentException("fromUtc must be <= toUtc.", nameof(fromUtc));

        long uploads = 0;
        long downloads = 0;
        long bytesUp = 0;
        long bytesDown = 0;
        long nukes = 0;
        long unNukes = 0;
        long pres = 0;
        long deletes = 0;
        long logins = 0;

        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(logFilePath))
        {
            return new AggregatedStats
            {
                FromUtc = fromUtc,
                ToUtc = toUtc
            };
        }

        using var fs = File.Open(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            SessionLogEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<SessionLogEntry>(line);
            }
            catch
            {
                // Ignore malformed lines; best-effort only.
                continue;
            }

            if (entry is null)
                continue;

            var ts = entry.Timestamp;
            if (ts < fromUtc || ts > toUtc)
                continue;

            switch (entry.EventType)
            {
                case FtpEventType.Upload:
                    uploads++;
                    if (entry.Bytes is long bu)
                        bytesUp += bu;
                    break;

                case FtpEventType.Download:
                    downloads++;
                    if (entry.Bytes is long bd)
                        bytesDown += bd;
                    break;

                case FtpEventType.Nuke:
                    nukes++;
                    break;

                case FtpEventType.Unnuke:
                    unNukes++;
                    break;

                case FtpEventType.Pre:
                    pres++;
                    break;

                case FtpEventType.Delete:
                    deletes++;
                    break;

                case FtpEventType.Login:
                    logins++;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(entry.User))
                users.Add(entry.User);
        }

        return new AggregatedStats
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Uploads = uploads,
            Downloads = downloads,
            BytesUploaded = bytesUp,
            BytesDownloaded = bytesDown,
            Nukes = nukes,
            UnNukes = unNukes,
            Pres = pres,
            Deletes = deletes,
            Logins = logins,
            UniqueUsers = users.Count
        };
    }

    /// <summary>
    /// Serialize stats to a compact JSON payload suitable for SITE responses or HTTP.
    /// </summary>
    public static string ToJsonPayload(string windowName, AggregatedStats stats)
    {
        var payload = new
        {
            window = windowName,
            fromUtc = stats.FromUtc,
            toUtc = stats.ToUtc,
            uploads = stats.Uploads,
            downloads = stats.Downloads,
            bytesUploaded = stats.BytesUploaded,
            bytesDownloaded = stats.BytesDownloaded,
            nukes = stats.Nukes,
            unNukes = stats.UnNukes,
            pres = stats.Pres,
            deletes = stats.Deletes,
            logins = stats.Logins,
            uniqueUsers = stats.UniqueUsers
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }
}