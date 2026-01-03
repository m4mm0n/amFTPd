using amFTPd.Core.Dupe;
using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Mappers;

public sealed class DupeImportMapper
{
    public DupeImportStats Apply(
        IEnumerable<ImportedDupeRecord> records,
        IDupeStore store,
        DupeImportMode mode,
        bool dryRun)
    {
        var stats = new DupeImportStats();

        foreach (var r in records)
        {
            stats.Total++;

            var existing = store.Find(r.Section, r.Release);

            if (existing is not null)
            {
                if (mode == DupeImportMode.Skip)
                {
                    stats.Skipped++;
                    continue;
                }

                if (mode == DupeImportMode.Merge)
                {
                    // keep earliest FirstSeen, max size
                    var merged = existing with
                    {
                        FirstSeen = r.FirstSeen < existing.FirstSeen
                            ? r.FirstSeen
                            : existing.FirstSeen,
                        TotalBytes = Math.Max(existing.TotalBytes, r.TotalBytes),
                        IsNuked = existing.IsNuked || r.IsNuked,
                        NukeReason = existing.NukeReason ?? r.NukeReason
                    };

                    if (!dryRun)
                        store.Upsert(merged);

                    stats.Updated++;
                    if (merged.IsNuked)
                        stats.Nuked++;

                    continue;
                }

                if (mode == DupeImportMode.Overwrite)
                {
                    if (!dryRun)
                        store.Upsert(ToEntry(r));

                    stats.Updated++;
                    if (r.IsNuked)
                        stats.Nuked++;

                    continue;
                }
            }

            // new entry
            if (!dryRun)
                store.Upsert(ToEntry(r));

            stats.Inserted++;
            if (r.IsNuked)
                stats.Nuked++;
        }

        return stats;
    }

    private static DupeEntry ToEntry(ImportedDupeRecord r)
        => new()
        {
            SectionName = r.Section,
            ReleaseName = r.Release,
            FirstSeen = r.FirstSeen,
            TotalBytes = r.TotalBytes,
            UploaderGroup = r.Group,
            IsNuked = r.IsNuked,
            NukeReason = r.NukeReason,
            NukeMultiplier = (int)Math.Round(r.NukeMultiplier)
        };
}