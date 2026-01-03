namespace amFTPd.Core.Dupe;

/// <summary>
/// Provides methods for mapping a DupeRelease object and associated data to a DupeEntry instance.
/// </summary>
/// <remarks>This static class is intended to facilitate the conversion of release information into a standardized
/// entry format for further processing or storage. All members are static and thread safe.</remarks>
public static class DupeEntryMapper
{
    public static DupeEntry ToEntry(
        DupeRelease r,
        string virtualPath) =>
        new()
        {
            SectionName = r.Section,
            ReleaseName = r.ReleaseName,
            VirtualPath = virtualPath,
            TotalBytes = r.TotalBytes,
            FirstSeen = r.FirstSeen,
            LastUpdated = r.LastUpdated,
            UploaderGroup = r.Group,
            IsNuked = r.IsNuked,
            NukeReason = r.NukeReason,
            NukeMultiplier = (int)Math.Round(r.NukeMultiplier)
        };
}