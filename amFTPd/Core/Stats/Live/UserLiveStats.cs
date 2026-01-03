namespace amFTPd.Core.Stats.Live;

/// <summary>
/// Represents real-time usage statistics for a user, including activity counts and data transfer metrics.
/// </summary>
public sealed class UserLiveStats
{
    public string UserName { get; init; } = "";
    public string? CurrentIpKey;

    public long Uploads;
    public long Downloads;
    public long BytesUploaded;
    public long BytesDownloaded;

    public int ActiveSessions;
}