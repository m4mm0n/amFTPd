namespace amFTPd.Core.Monitoring;

public sealed class StatusRatesPayload
{
    public double CommandsPerSecond { get; init; }
    public double AverageTransferDurationMs { get; init; }
}