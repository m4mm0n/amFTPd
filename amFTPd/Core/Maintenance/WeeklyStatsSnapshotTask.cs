/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           WeeklyStatsSnapshotTask.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24
 *
 *  Description:
 *      Scheduled task that archives weekly upload/download statistics to a JSON snapshot file
 *      every Sunday at midnight UTC (written on the first Monday pass-through each week).
 *      Snapshot files are written to {configDir}/stats/weekly-YYYY-Www.json.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Globalization;
using System.Text.Json;
using amFTPd.Core.Stats;
using amFTPd.Logging;

namespace amFTPd.Core.Maintenance;

/// <summary>
/// Archives weekly stats to <c>stats/weekly-{year}-W{week:D2}.json</c> once per week,
/// triggered on the first Monday pass-through after midnight UTC.
/// </summary>
public sealed class WeeklyStatsSnapshotTask : IScheduledTask
{
    // Track the last ISO week (year + week number) for which a snapshot was written.
    // Volatile so that reads across threads are coherent without requiring a lock.
    private volatile string _lastSnapshotWeekKey = string.Empty;

    public string Name => "WeeklyStatsSnapshot";

    /// <summary>Poll once per hour; the body does the real calendar gating.</summary>
    public TimeSpan Interval => TimeSpan.FromHours(1);

    public Task RunAsync(ScheduledTaskContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.DateTime);

        // Trigger on Monday — the first check after a week boundary has passed.
        if (today.DayOfWeek != DayOfWeek.Monday)
            return Task.CompletedTask;

        // ISO week key for *last* week (i.e. the week that just ended on Sunday).
        var lastSunday = today.AddDays(-1);
        var weekKey = GetIsoWeekKey(lastSunday);

        // Idempotent: only write once per week, even if the process restarts mid-Monday.
        if (_lastSnapshotWeekKey == weekKey)
            return Task.CompletedTask;

        try
        {
            // Window: Monday 00:00 … Sunday 23:59:59 UTC of the week just ended.
            var to = new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, TimeSpan.Zero);
            var from = to.AddDays(-7);

            var logPath = SessionLogStatsService.GetDefaultLogPath(context.Runtime);
            var stats = SessionLogStatsService.Compute(logPath, from, to);

            var configDir = Path.GetDirectoryName(context.Runtime.ConfigFilePath)
                            ?? AppContext.BaseDirectory;
            var statsDir = Path.Combine(configDir, "stats");
            Directory.CreateDirectory(statsDir);

            var filePath = Path.Combine(statsDir, $"weekly-{weekKey}.json");
            var json = SessionLogStatsService.ToJsonPayload($"weekly-{weekKey}", stats);
            File.WriteAllText(filePath, json);

            context.Log.Log(FtpLogLevel.Info,
                $"[STATS] Weekly snapshot written: {filePath} " +
                $"(uploads={stats.Uploads}, downloads={stats.Downloads})");

            _lastSnapshotWeekKey = weekKey;
        }
        catch (Exception ex)
        {
            context.Log.Log(FtpLogLevel.Warn,
                $"[STATS] Failed to write weekly snapshot: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>Returns an ISO 8601-style week key, e.g. "2026-W17".</summary>
    private static string GetIsoWeekKey(DateOnly date)
    {
        var week = ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
        var year = ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue));
        return $"{year}-W{week:D2}";
    }
}
