namespace amFTPd.Core.Pre;

/// <summary>
/// Approval state for a pre entry in the pre log or approval queue.
/// </summary>
public enum PreStatus
{
    /// <summary>Pre is live (default; no approval required or approval granted).</summary>
    Approved,
    /// <summary>Pre is waiting for staff approval.</summary>
    Pending,
    /// <summary>Pre was denied by staff.</summary>
    Denied
}

/// <summary>
/// Represents a record of a pre-release entry, including metadata such as section, release name,
/// virtual path, user, group, file metadata, and approval state.
/// </summary>
/// <param name="Section">The section this pre belongs to.</param>
/// <param name="ReleaseName">The release name (e.g. "Artist-Album-2025-GROUP").</param>
/// <param name="VirtualPath">Virtual path at which the release lives.</param>
/// <param name="User">The user who issued the PRE command.</param>
/// <param name="Timestamp">UTC timestamp when the pre was created.</param>
public sealed record PreEntry(
    string Section,
    string ReleaseName,
    string VirtualPath,
    string User,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// The primary group of the user who issued the pre.
    /// Null if unknown (e.g. loaded from legacy data).
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Total number of files in the release at pre time.
    /// 0 if not available.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Total size of the release in bytes at pre time.
    /// 0 if not available.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Space-separated tags assigned to this pre (e.g. "repack proper").
    /// Null or empty if no tags.
    /// </summary>
    public string? Tags { get; init; }

    /// <summary>
    /// Approval status. Approved (default), Pending, or Denied.
    /// </summary>
    public PreStatus Status { get; init; } = PreStatus.Approved;

    /// <summary>
    /// The siteop who approved or denied this pre, if applicable.
    /// </summary>
    public string? ReviewedBy { get; init; }

    /// <summary>
    /// UTC timestamp when this pre was approved or denied.
    /// </summary>
    public DateTimeOffset? ReviewedAtUtc { get; init; }

    /// <summary>
    /// Optional reason supplied when denying a pre.
    /// </summary>
    public string? DenyReason { get; init; }
}
