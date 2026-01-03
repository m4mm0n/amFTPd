namespace amFTPd.Core.Stats;

/// <summary>
/// Represents a snapshot of command and data transfer rates over a specific interval.
/// </summary>
/// <remarks>This record provides instantaneous rate metrics, typically used for monitoring or reporting
/// system activity. All rate values are measured per second and reflect the state at the time the snapshot was
/// taken.</remarks>
public sealed record StatsRateSnapshot
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public TimeSpan Window { get; init; }

    // Rates
    public double CommandsPerSecond { get; init; }
    public double UploadBytesPerSecond { get; init; }
    public double DownloadBytesPerSecond { get; init; }
    public double TransfersPerSecond { get; init; }

    // Convenience
    public bool IsEmpty => Window <= TimeSpan.Zero;

    public static StatsRateSnapshot Empty { get; } = new();
}