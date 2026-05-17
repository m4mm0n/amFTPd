namespace amFTPd.Core.Stats;

public sealed class PerfSnapshot
{
    public long ActiveConnections { get; init; }
    public long TotalConnections { get; init; }

    public long ActiveTransfers { get; init; }
    public long TotalTransfers { get; init; }

    public long BytesUploaded { get; init; }
    public long BytesDownloaded { get; init; }

    public long FailedLogins { get; init; }
    public long AbortedTransfers { get; init; }

    public long TotalCommands { get; init; }

    public double AverageTransferMilliseconds { get; init; }

    public long MaxConcurrentTransfers { get; init; }

    // Scene-specific event counters
    public long TotalNukes { get; init; }
    public long TotalUnnukes { get; init; }
    public long TotalPres { get; init; }

    // Server uptime (set by StatusEndpoint from its start time)
    public double UptimeSeconds { get; init; }
}
