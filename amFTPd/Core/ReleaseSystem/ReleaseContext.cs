namespace amFTPd.Core.ReleaseSystem;

/// <summary>
/// Represents contextual information about a release, including its identity, state, and associated metadata.
/// </summary>
/// <remarks>A release context encapsulates details such as the section, release name, state, timestamps, and file
/// statistics. It is typically used to track and manage the lifecycle and properties of a release within a system.
/// Instances of this class are immutable with respect to identifying properties (Section, ReleaseName, FirstSeen), but
/// allow updates to state and metadata as the release progresses.</remarks>
public sealed class ReleaseContext
{
    public string Section { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;

    public string VirtualPath
        => $"/{Section}/{ReleaseName}";

    public ReleaseState State { get; set; } = ReleaseState.New;

    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastUpdated { get; set; }

    // Aggregated info (used later by zipscript)
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }

    public bool HasSfv { get; set; }
    public bool HasNfo { get; set; }
    public bool HasDiz { get; set; }

    public bool IsVisible
        => State != ReleaseState.Archived;

    public bool IsComplete =>
        HasSfv && HasNfo && FileCount > 0;

    public string DisplayName =>
        State == ReleaseState.Incomplete
            ? $"{ReleaseName} [INCOMPLETE]"
            : ReleaseName;
}