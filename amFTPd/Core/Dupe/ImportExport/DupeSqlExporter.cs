using amFTPd.Core.Import;
using System.Data.Common;

namespace amFTPd.Core.Dupe.ImportExport;

/// <summary>
/// Provides functionality to export a collection of duplicate scene entries to a SQL database.
/// </summary>
/// <remarks>This class is static and cannot be instantiated. It is intended for use in scenarios where duplicate
/// scene release information needs to be persisted to a database table named 'dupes'.</remarks>
public static class DupeSqlExporter
{
    public static void Export(
        IEnumerable<SceneDupeEntry> entries,
        DbConnection conn)
    {
        conn.Open();

        var list = entries as IReadOnlyCollection<SceneDupeEntry>
                   ?? entries.ToList();

        var progress = ImportProgressRegistry.Start(
            "DUPE SQL EXPORT",
            list.Count);

        try
        {
            foreach (var e in list)
            {
                if (progress.CancelRequested)
                    break;

                using var cmd = conn.CreateCommand();
                cmd.CommandText = BuildUpsertSql(conn);

                cmd.AddParam("@s", e.Section);
                cmd.AddParam("@r", e.ReleaseName);
                cmd.AddParam("@g", e.Group);
                cmd.AddParam("@fs", e.ReleaseDate.ToUnixTimeSeconds());
                cmd.AddParam("@tb", e.TotalBytes);
                cmd.AddParam("@fc", e.FileCount);
                cmd.AddParam("@n", e.IsNuked ? 1 : 0);
                cmd.AddParam("@nr", e.NukeReason);
                cmd.AddParam("@nm", e.NukeMultiplier);

                cmd.ExecuteNonQuery();

                progress.Processed++;
            }
        }
        finally
        {
            ImportProgressRegistry.Finish();
        }
    }

    public static string BuildUpsertSql(DbConnection conn)
    {
        var name = conn.GetType().Name;

        if (name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return @"
INSERT OR REPLACE INTO dupes
(section, release, grp, first_seen, total_bytes, file_count,
 is_nuked, nuke_reason, nuke_multiplier)
VALUES (@s,@r,@g,@fs,@tb,@fc,@n,@nr,@nm)";

        if (name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return @"
INSERT INTO dupes
(section, release, grp, first_seen, total_bytes, file_count,
 is_nuked, nuke_reason, nuke_multiplier)
VALUES (@s,@r,@g,@fs,@tb,@fc,@n,@nr,@nm)
ON CONFLICT (section, release)
DO UPDATE SET
 grp=EXCLUDED.grp,
 total_bytes=EXCLUDED.total_bytes,
 file_count=EXCLUDED.file_count,
 is_nuked=EXCLUDED.is_nuked,
 nuke_reason=EXCLUDED.nuke_reason,
 nuke_multiplier=EXCLUDED.nuke_multiplier";

        if (name.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            return @"
INSERT INTO dupes
(section, release, grp, first_seen, total_bytes, file_count,
 is_nuked, nuke_reason, nuke_multiplier)
VALUES (@s,@r,@g,@fs,@tb,@fc,@n,@nr,@nm)
ON DUPLICATE KEY UPDATE
 grp=VALUES(grp),
 total_bytes=VALUES(total_bytes),
 file_count=VALUES(file_count),
 is_nuked=VALUES(is_nuked),
 nuke_reason=VALUES(nuke_reason),
 nuke_multiplier=VALUES(nuke_multiplier)";

        throw new NotSupportedException($"Unknown SQL provider: {name}");
    }
}