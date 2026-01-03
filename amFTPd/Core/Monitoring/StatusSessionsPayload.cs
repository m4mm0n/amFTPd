namespace amFTPd.Core.Monitoring;

/// <summary>
/// Gets the number of active sessions represented by the payload.
/// </summary>
public sealed class StatusSessionsPayload
{
    public int Active { get; init; }
}