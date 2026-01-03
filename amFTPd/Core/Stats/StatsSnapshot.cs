namespace amFTPd.Core.Stats;

/// <summary>
/// Represents an immutable snapshot of server statistics at a specific point in time.
/// </summary>
/// <remarks>This record provides a consistent view of various server metrics, including connection counts,
/// command activity, transfer statistics, and security-related events. All properties are initialized at the time the
/// snapshot is created and do not change thereafter. Use this type to capture and analyze server state for monitoring,
/// diagnostics, or reporting purposes.</remarks>
public sealed record StatsSnapshot
{
    public DateTimeOffset Timestamp { get; init; }

    // --- connections ---
    public long ActiveConnections { get; init; }
    public long TotalConnections { get; init; }

    // --- commands ---
    public long TotalCommands { get; init; }
    public double CommandsPerSecond { get; init; }

    // --- transfers ---
    public long ActiveTransfers { get; init; }
    public long TotalTransfers { get; init; }
    public double AverageTransferDurationMs { get; init; }

    // --- abuse / security ---
    public long FailedLogins { get; init; }
    public long AbortedTransfers { get; init; }

    // --- volume ---
    public long BytesUploaded { get; init; }
    public long BytesDownloaded { get; init; }

    // --- rates ---
    public double UploadBytesPerSecond { get; init; }
    public double DownloadBytesPerSecond { get; init; }

    // --- utilization ---
    public int OnlineUsers { get; init; }

    public static StatsSnapshot Empty { get; } = new()
    {
        Timestamp = DateTimeOffset.MinValue
    };

    public static StatsSnapshot FromCounters(
        PerfSnapshot current,
        PerfSnapshot? previous,
        TimeSpan window)
    {
        var cmdDelta = previous is null
            ? 0
            : current.TotalCommands - previous.TotalCommands;
        var upDelta = previous is null ? 0 : current.BytesUploaded - previous.BytesUploaded;
        var downDelta = previous is null ? 0 : current.BytesDownloaded - previous.BytesDownloaded;

        return new StatsSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,

            ActiveConnections = current.ActiveConnections,
            TotalConnections = current.TotalConnections,

            TotalCommands = current.TotalCommands,
            CommandsPerSecond = window.TotalSeconds > 0
                ? cmdDelta / window.TotalSeconds
                : 0,

            ActiveTransfers = current.ActiveTransfers,
            TotalTransfers = current.TotalTransfers,
            AverageTransferDurationMs = current.AverageTransferMilliseconds,

            FailedLogins = current.FailedLogins,
            AbortedTransfers = current.AbortedTransfers,

            BytesUploaded = current.BytesUploaded,
            BytesDownloaded = current.BytesDownloaded,

            UploadBytesPerSecond = window.TotalSeconds > 0 ? upDelta / window.TotalSeconds : 0,
            DownloadBytesPerSecond = window.TotalSeconds > 0 ? downDelta / window.TotalSeconds : 0
        };
    }
}