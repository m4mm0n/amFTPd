namespace amFTPd.Core.Import.Records;

/// <summary>
/// Represents a record of a duplicate release imported from an external source, including metadata such as section,
/// release name, group, and nuke status.
/// </summary>
/// <remarks>This type is typically used to capture information about releases that have been identified as
/// duplicates during import operations. It includes details relevant for tracking, auditing, or further processing,
/// such as when the release was first seen, its size, and any nuke-related information. All properties are immutable
/// and set during initialization.</remarks>
public sealed class ImportedDupeRecord
{
    public string Section { get; init; } = string.Empty;
    public string Release { get; init; } = string.Empty;
    public string Group { get; init; } = "UNKNOWN";

    public DateTimeOffset FirstSeen { get; init; }
    public long TotalBytes { get; init; }

    public bool IsNuked { get; init; }
    public string? NukeReason { get; init; }
    public double NukeMultiplier { get; init; }
}