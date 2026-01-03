namespace amFTPd.Core.Monitoring;

/// <summary>
/// Represents a snapshot of current system status, including session, transfer, rolling, rate, and IP statistics.
/// </summary>
/// <remarks>This type aggregates multiple status payloads to provide a comprehensive view of the system's
/// operational state at a specific point in time. All properties are immutable and set during object
/// initialization.</remarks>
public sealed class StatusPayload
{
    public DateTimeOffset NowUtc { get; init; }

    public StatusSessionsPayload Sessions { get; init; } = new();
    public StatusTransfersPayload Transfers { get; init; } = new();
    public StatusRollingPayload Rolling { get; init; } = new();

    public StatusRatesPayload? Rates { get; init; }

    public IDictionary<string, IpStatsPayload>? Ips { get; init; }
}