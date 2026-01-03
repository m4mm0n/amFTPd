namespace amFTPd.Core.Dupe;

/// <summary>
/// Portable scene-style dupe entry.
/// This format is independent of amFTPd and contains no VFS paths.
/// </summary>
public sealed record SceneDupeEntry
{
    public string Section { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;

    public DateTimeOffset ReleaseDate { get; init; }

    public long TotalBytes { get; init; }
    public int FileCount { get; init; }
    public int ArchiveCount { get; init; }

    public bool HasSfv { get; init; }
    public bool HasNfo { get; init; }
    public bool HasDiz { get; init; }

    /// <summary>
    /// CRC32 per archive file (filename → crc).
    /// Always populated.
    /// </summary>
    public IReadOnlyDictionary<string, uint> Crc32 { get; init; }
        = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

    public bool IsNuked { get; init; }
    public string? NukeReason { get; init; }
    public double NukeMultiplier { get; init; }
}