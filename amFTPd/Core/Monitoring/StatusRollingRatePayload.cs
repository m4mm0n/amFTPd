namespace amFTPd.Core.Monitoring;

/// <summary>
/// Represents rolling rate statistics over multiple time intervals for a status measurement.
/// </summary>
/// <remarks>This type is typically used to convey short-term and medium-term rate values, such as those used in
/// monitoring or analytics scenarios. All values are immutable after initialization.</remarks>
public sealed class StatusRollingRatePayload
{
    public double S5 { get; init; }
    public double M1 { get; init; }
    public double M5 { get; init; }
}