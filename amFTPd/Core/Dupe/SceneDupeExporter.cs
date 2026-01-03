using System.Text.Json;

namespace amFTPd.Core.Dupe;

/// <summary>
/// Provides functionality to export scene duplicate database information to a JSON file.
/// </summary>
/// <remarks>This class contains static methods for exporting data from an <see cref="IDupeStore"/> to a file in
/// JSON format. It is intended for use in scenarios where scene duplicate information needs to be persisted or shared
/// in a standardized format. This class cannot be instantiated.</remarks>
public static class SceneDupeExporter
{
    public static void Export(
        IEnumerable<DupeRelease> releases,
        string path)
    {
        var outList = releases.Select(r =>
                new SceneDupeEntry
                {
                    Section = r.Section,
                    ReleaseName = r.ReleaseName,
                    Group = r.Group,
                    ReleaseDate = r.FirstSeen,
                    TotalBytes = r.TotalBytes,
                    FileCount = r.FileCount,
                    ArchiveCount = r.ArchiveCount,
                    HasSfv = r.HasSfv,
                    HasNfo = r.HasNfo,
                    HasDiz = r.HasDiz,
                    Crc32 = r.Crc32,
                    IsNuked = r.IsNuked,
                    NukeReason = r.NukeReason,
                    NukeMultiplier = r.NukeMultiplier
                })
            .ToList();

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(outList));
    }
}