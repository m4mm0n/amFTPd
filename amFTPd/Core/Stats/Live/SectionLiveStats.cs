namespace amFTPd.Core.Stats.Live;

/// <summary>
/// Represents live statistics for a specific section, including upload and download counts, data volume, and the number
/// of active users.
/// </summary>
public sealed class SectionLiveStats
{
    public string SectionName { get; init; } = "";

    public long Uploads;
    public long Downloads;
    public long BytesUploaded;
    public long BytesDownloaded;

    public int ActiveUsers;

    // ------------------------------------------------------------
    // Bandwidth enforcement (rolling window)
    // ------------------------------------------------------------
    public long BytesWindow;
    public DateTimeOffset BandwidthWindowUtc = DateTimeOffset.UtcNow;

    public bool IsBlocked;
    public string? BlockReason;
    public DateTimeOffset? BlockedUntilUtc;

    public bool IsCurrentlyBlocked =>
        IsBlocked &&
        (!BlockedUntilUtc.HasValue || BlockedUntilUtc > DateTimeOffset.UtcNow);
}