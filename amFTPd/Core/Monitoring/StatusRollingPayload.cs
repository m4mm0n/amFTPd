namespace amFTPd.Core.Monitoring;

/// <summary>
/// Represents the rolling status payload, including transfer rate information.
/// </summary>
public sealed class StatusRollingPayload
{
    public StatusRollingRatePayload TransfersPerSecond { get; init; } = new();
}