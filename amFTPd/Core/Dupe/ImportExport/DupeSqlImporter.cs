using System.Data.Common;

namespace amFTPd.Core.Dupe.ImportExport;

/// <summary>
/// Provides methods for importing dupe entries from a SQL database.
/// </summary>
/// <remarks>This class is intended for use with databases containing a 'dupes' table structured to match the
/// SceneDupeEntry type. All members are static and thread safety depends on the provided database connection.</remarks>
public static class DupeSqlImporter
{
    public static IEnumerable<SceneDupeEntry> Import(DbConnection conn)
    {
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM dupes";

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            yield return new SceneDupeEntry
            {
                Section = rd.GetString(0),
                ReleaseName = rd.GetString(1),
                Group = rd.GetString(2),
                ReleaseDate = DateTimeOffset.FromUnixTimeSeconds(rd.GetInt64(3)),
                TotalBytes = rd.GetInt64(4),
                FileCount = rd.GetInt32(5),
                IsNuked = rd.GetInt32(6) != 0,
                NukeReason = rd.IsDBNull(7) ? null : rd.GetString(7),
                NukeMultiplier = rd.GetDouble(8)
            };
        }
    }

}