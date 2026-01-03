namespace amFTPd.Core.Dupe;

/// <summary>
/// Represents metadata for a duplicate binary record, including section, release, group, file statistics, timestamps,
/// and nuke status information.
/// </summary>
internal sealed class BinaryDupeMetaRecord
{
    public string Section = "";
    public string Release = "";
    public string Group = "";

    public long TotalBytes;
    public int FileCount;
    public int ArchiveCount;

    public long FirstSeenUnix;
    public long LastUpdatedUnix;

    public bool IsNuked;
    public double NukeMultiplier;
    public string? NukeReason;

    // Pointer into CRC file
    public long CrcOffset;
    public int CrcCount;
}