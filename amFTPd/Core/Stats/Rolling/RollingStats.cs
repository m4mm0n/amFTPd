namespace amFTPd.Core.Stats.Rolling;

/// <summary>
/// Provides rolling statistical counters for commands, data transfers, and bandwidth usage over multiple time
/// intervals.
/// </summary>
/// <remarks>Use this class to monitor recent activity metrics, such as the number of commands executed or bytes
/// transferred, within configurable rolling windows (5 seconds, 1 minute, and 5 minutes). Each counter tracks its
/// respective metric over the specified interval, enabling real-time analysis of short-term trends. This class is
/// thread-safe if the underlying RollingCounter implementation is thread-safe.</remarks>
public sealed class RollingStats
{
    public RollingCounter Transfers5s { get; } =
        new(TimeSpan.FromSeconds(5));
    public RollingCounter Transfers1m { get; } =
        new(TimeSpan.FromMinutes(1));
    public RollingCounter Transfers5m { get; } =
        new(TimeSpan.FromMinutes(5));

    public RollingCounter UploadBytes5s { get; } =
        new(TimeSpan.FromSeconds(5));
    public RollingCounter UploadBytes1m { get; } =
        new(TimeSpan.FromMinutes(1));
    public RollingCounter UploadBytes5m { get; } =
        new(TimeSpan.FromMinutes(5));

    public RollingCounter DownloadBytes5s { get; } =
        new(TimeSpan.FromSeconds(5));
    public RollingCounter DownloadBytes1m { get; } =
        new(TimeSpan.FromMinutes(1));
    public RollingCounter DownloadBytes5m { get; } =
        new(TimeSpan.FromMinutes(5));

    public RollingCounter Commands5s { get; } =
        new(TimeSpan.FromSeconds(5));
    public RollingCounter Commands1m { get; } =
        new(TimeSpan.FromMinutes(1));
    public RollingCounter Commands5m { get; } =
        new(TimeSpan.FromMinutes(5));
}