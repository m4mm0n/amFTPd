namespace amFTPd.Core.Dupe;

/// <summary>
/// Represents a record of a duplicate release, including metadata such as section, release name, group, file details,
/// and nuke status.
/// </summary>
/// <remarks>A DupeRelease instance tracks information about a specific release, including its associated files,
/// archive details, and any nuke status applied. This type is typically used to manage and query duplicate releases in
/// automated systems or databases. All properties are read-only except where explicitly noted, ensuring the integrity
/// of release data after creation.</remarks>
public sealed class DupeRelease
{
    public string Section { get; }
    public string ReleaseName { get; }
    public string Group { get; }

    public DateTimeOffset FirstSeen { get; private set; }
    public DateTimeOffset LastUpdated { get; private set; }

    public long TotalBytes { get; private set; }

    public int ArchiveCount { get; private set; }
    public int FileCount { get; private set; }

    public bool HasSfv { get; private set; }
    public bool HasNfo { get; private set; }
    public bool HasDiz { get; private set; }

    /// <summary>
    /// CRC32 for every archive file (filename → crc).
    /// Always populated.
    /// </summary>
    public Dictionary<string, uint> Crc32 { get; }
        = new(StringComparer.OrdinalIgnoreCase);

    public bool IsNuked { get; private set; }
    public string? NukeReason { get; private set; }
    public double NukeMultiplier { get; private set; }

    public DupeRelease(
        string section,
        string releaseName,
        string group,
        DateTimeOffset seen)
    {
        Section = section;
        ReleaseName = releaseName;
        Group = group;
        FirstSeen = seen;
        LastUpdated = seen;
    }

    // ------------------------------------------------------------
    // FILE INGESTION
    // ------------------------------------------------------------

    public void AddArchive(
        string fileName,
        long sizeBytes,
        uint crc)
    {
        if (Crc32.ContainsKey(fileName))
            return;

        Crc32[fileName] = crc;
        ArchiveCount++;
        FileCount++;
        TotalBytes += sizeBytes;
        Touch();
    }

    public void AddNonArchive(long sizeBytes)
    {
        FileCount++;
        TotalBytes += sizeBytes;
        Touch();
    }

    public void MarkSfvPresent()
        => HasSfv = true;

    public void MarkNfoPresent()
        => HasNfo = true;

    public void MarkDizPresent()
        => HasDiz = true;

    public bool HasCrc(string fileName)
        => Crc32.ContainsKey(fileName);

    public IReadOnlyCollection<string> ArchiveNames
        => Crc32.Keys;

    // ------------------------------------------------------------
    // NUKE
    // ------------------------------------------------------------

    public void Nuke(string reason, double multiplier)
    {
        IsNuked = true;
        NukeReason = reason;
        NukeMultiplier = multiplier;
        Touch();
    }

    public void Unnuke()
    {
        IsNuked = false;
        Touch();
    }

    private void Touch()
        => LastUpdated = DateTimeOffset.UtcNow;
}