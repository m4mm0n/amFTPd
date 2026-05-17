namespace amFTPd.Core.Dupe;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a record of a duplicate release, including metadata such as section, release name, group, file details,
/// and nuke status.
/// </summary>
/// <remarks>
/// A DupeRelease tracks metadata for a specific release, including file stats, nuke state, and archive CRC map.
/// </remarks>
public sealed class DupeRelease
{
    public DupeRelease()
    {
    }

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

    public string Section { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;

    [JsonInclude]
    public DateTimeOffset FirstSeen { get; private set; }
    [JsonInclude]
    public DateTimeOffset LastUpdated { get; private set; }

    [JsonInclude]
    public long TotalBytes { get; private set; }

    [JsonInclude]
    public int ArchiveCount { get; private set; }
    [JsonInclude]
    public int FileCount { get; private set; }

    [JsonInclude]
    public bool HasSfv { get; private set; }
    [JsonInclude]
    public bool HasNfo { get; private set; }
    [JsonInclude]
    public bool HasDiz { get; private set; }

    /// <summary>
    /// CRC32 for every archive file (filename → crc).
    /// Always populated.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, uint> Crc32 { get; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonInclude]
    public bool IsNuked { get; private set; }
    [JsonInclude]
    public string? NukeReason { get; private set; }
    [JsonInclude]
    public double NukeMultiplier { get; private set; }

    [JsonInclude]
    public Dictionary<string, long> NukePenalties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
        NukeReason = null;
        NukeMultiplier = 0;
        NukePenalties.Clear();
        Touch();
    }

    public void SetNukePenalties(IReadOnlyDictionary<string, long> penalties)
    {
        NukePenalties = new Dictionary<string, long>(penalties, StringComparer.OrdinalIgnoreCase);
        Touch();
    }

    private void Touch()
        => LastUpdated = DateTimeOffset.UtcNow;
}
