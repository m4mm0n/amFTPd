/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           MonthlyStatsSnapshotTask.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24
 *
 *  Description:
 *      Scheduled task that archives monthly upload/download statistics to a JSON snapshot file
 *      on the 1st of each month at midnight UTC (written on the first hourly pass-through
 *      on the 1st).  Snapshot files are written to {configDir}/stats/monthly-YYYY-MM.json.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text.Json;
using amFTPd.Core.Stats;
using amFTPd.Logging;

namespace amFTPd.Core.Maintenance;

/// <summary>
/// Archives monthly stats to <c>stats/monthly-{year}-{month:D2}.json</c> once per month,
/// triggered on the first hourly pass-through on the 1st of each month.
/// </summary>
public sealed class MonthlyStatsSnapshotTask : IScheduledTask
{
    // Track the last "YYYY-MM" string for which a snapshot was written.
    private volatile string _lastSnapshotMonthKey = string.Empty;

    public string Name => "MonthlyStatsSnapshot";

    /// <summary>Poll once per hour; the body does the real calendar gating.</summary>
    public TimeSpan Interval => TimeSpan.FromHours(1);

    public Task RunAsync(ScheduledTaskContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.DateTime);

        // Trigger on the 1st of the month — first hourly pass after a month boundary.
        if (today.Day != 1)
            return Task.CompletedTask;

        // The month we want to snapshot is the one that just ended.
        var lastMonth = today.AddMonths(-1);
        var monthKey = $"{lastMonth.Year}-{lastMonth.Month:D2}";

        // Idempotent: only write once per month.
        if (_lastSnapshotMonthKey == monthKey)
            return Task.CompletedTask;

        try
        {
            // Window: midnight of 1st to midnight of 1st (exclusive end — full calendar month UTC).
            var to = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var from = to.AddMonths(-1);

            var logPath = SessionLogStatsService.GetDefaultLogPath(context.Runtime);
            var stats = SessionLogStatsService.Compute(logPath, from, to);

            var configDir = Path.GetDirectoryName(context.Runtime.ConfigFilePath)
                            ?? AppContext.BaseDirectory;
            var statsDir = Path.Combine(configDir, "stats");
            Directory.CreateDirectory(statsDir);

            var filePath = Path.Combine(statsDir, $"monthly-{monthKey}.json");
            var json = SessionLogStatsService.ToJsonPayload($"monthly-{monthKey}", stats);
            File.WriteAllText(filePath, json);

            context.Log.Log(FtpLogLevel.Info,
                $"[STATS] Monthly snapshot written: {filePath} " +
                $"(uploads={stats.Uploads}, downloads={stats.Downloads})");

            _lastSnapshotMonthKey = monthKey;
        }
        catch (Exception ex)
        {
            context.Log.Log(FtpLogLevel.Warn,
                $"[STATS] Failed to write monthly snapshot: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }
}
