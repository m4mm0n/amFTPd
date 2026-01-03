using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Stats.Live;

/// <summary>
/// Represents real-time network statistics for a specific IP address, including upload and download activity, data
/// transferred, and active sessions.
/// </summary>
public sealed class IpLiveStats
{
    public string Ip { get; init; } = "";

    public long Uploads;
    public long Downloads;
    public long BytesUploaded;
    public long BytesDownloaded;

    public int ActiveSessions;

    // ------------------------------------------------------------------
    // Rolling bandwidth windows (for abuse detection)
    // ------------------------------------------------------------------

    public long UploadBytes5m;
    public long DownloadBytes5m;

    public DateTimeOffset RatioWindowUtc = DateTimeOffset.UtcNow;

    // Enforcement state
    public bool IsBlocked;
    public string? BlockReason;
    public DateTimeOffset? BlockedUntilUtc;

    // Existing fields (already referenced elsewhere)
    public long BytesWindow;
    public DateTimeOffset BandwidthWindowUtc;

    public long CommandsLastWindow;
    public DateTimeOffset LastCommandWindowUtc;

    public int FailedLogins;
    public DateTimeOffset? LastFailedLoginUtc;

    public bool IsCurrentlyBlocked =>
        IsBlocked &&
        (!BlockedUntilUtc.HasValue || BlockedUntilUtc > DateTimeOffset.UtcNow);

    // --- Reputation ---------------------------------------------------
    public FtpSessionReputation Reputation = FtpSessionReputation.Good;
    public DateTimeOffset LastViolationUtc;
}