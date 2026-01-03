namespace amFTPd.Core.Stats;

/// <summary>
/// Provides methods for calculating per-second rates of statistics over a specified time window between two snapshots.
/// </summary>
/// <remarks>This class is intended for use with statistics that are collected at discrete points in time. It
/// enables calculation of rates such as commands per second, bytes transferred per second, and similar metrics by
/// comparing two snapshots taken at different times. All methods are static and thread safe.</remarks>
public static class StatsRateCalculator
{
    public static StatsRateSnapshot Compute(
        StatsSnapshot older,
        StatsSnapshot newer)
    {
        if (older.Timestamp == default ||
            newer.Timestamp == default ||
            newer.Timestamp <= older.Timestamp)
        {
            return StatsRateSnapshot.Empty;
        }

        var window = newer.Timestamp - older.Timestamp;
        var seconds = window.TotalSeconds;
        if (seconds <= 0)
            return StatsRateSnapshot.Empty;

        return new StatsRateSnapshot
        {
            From = older.Timestamp,
            To = newer.Timestamp,
            Window = window,

            CommandsPerSecond =
                (newer.TotalCommands - older.TotalCommands) / seconds,

            UploadBytesPerSecond =
                (newer.BytesUploaded - older.BytesUploaded) / seconds,

            DownloadBytesPerSecond =
                (newer.BytesDownloaded - older.BytesDownloaded) / seconds,

            TransfersPerSecond =
                (newer.TotalTransfers - older.TotalTransfers) / seconds
        };
    }
}