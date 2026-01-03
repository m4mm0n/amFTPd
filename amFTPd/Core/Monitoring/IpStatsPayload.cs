namespace amFTPd.Core.Monitoring;

public sealed class IpStatsPayload
{
    public int ActiveSessions { get; init; }
    public long Uploads { get; init; }
    public long Downloads { get; init; }
    public long BytesUploaded { get; init; }
    public long BytesDownloaded { get; init; }
}