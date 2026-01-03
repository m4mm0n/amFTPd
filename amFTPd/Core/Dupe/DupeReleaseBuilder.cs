using amFTPd.Core.Scene;
using amFTPd.Core.Zipscript;

namespace amFTPd.Core.Dupe;

/// <summary>
/// Provides factory methods for creating instances of the DupeRelease class from external release status data.
/// </summary>
/// <remarks>This static class is intended to facilitate the construction of DupeRelease objects based on
/// information obtained from zipscript processing or similar sources. It encapsulates the logic required to interpret
/// release status and associated files, ensuring that DupeRelease instances are populated consistently.</remarks>
public static class DupeReleaseBuilder
{
    public static DupeRelease FromZipscript(
        ZipscriptReleaseStatus status,
        string groupName)
    {
        if (status is null)
            throw new ArgumentNullException(nameof(status));

        var releaseName = Path.GetFileName(status.ReleasePath);

        var dupe = new DupeRelease(
            section: status.SectionName,
            releaseName: releaseName,
            group: groupName,
            seen: status.Started
        );

        // NUKE STATE
        if (status.IsNuked && status.NukeReason is not null)
        {
            dupe.Nuke(
                status.NukeReason,
                status.NukeMultiplier ?? 1.0
            );
        }

        // FILE INGESTION
        foreach (var file in status.Files)
        {
            var name = file.FileName;

            if (SceneFileClassifier.IsSfv(name))
            {
                dupe.MarkSfvPresent();
                continue;
            }

            if (SceneFileClassifier.IsNfo(name))
            {
                dupe.MarkNfoPresent();
                dupe.AddNonArchive(file.SizeBytes);
                continue;
            }

            if (SceneFileClassifier.IsDiz(name))
            {
                dupe.MarkDizPresent();
                dupe.AddNonArchive(file.SizeBytes);
                continue;
            }

            if (SceneFileClassifier.IsArchive(name))
            {
                var crc =
                    file.ActualCrc
                    ?? file.ExpectedCrc;

                if (crc.HasValue)
                {
                    dupe.AddArchive(
                        name,
                        file.SizeBytes,
                        crc.Value);
                }

                continue;
            }

            // everything else
            dupe.AddNonArchive(file.SizeBytes);
        }

        return dupe;
    }
}