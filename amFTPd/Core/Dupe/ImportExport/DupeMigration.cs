using amFTPd.Core.Import;
using System.Data.Common;

namespace amFTPd.Core.Dupe.ImportExport;

/// <summary>
/// Provides static methods for migrating dupe data between file-based and SQL-based storage formats.
/// </summary>
/// <remarks>This class cannot be instantiated. All members are static and are intended to facilitate import and
/// export operations between dupe files and SQL databases.</remarks>
public static class DupeMigration
{
    public static void DupefileToSql(
        string dupefile,
        DbConnection conn)
    {
        var entries = DupeFileImporter.Import(dupefile);
        DupeSqlExporter.Export(entries, conn);
    }

    public static IReadOnlyList<SceneDupeEntry> SqlToRuntime(
        DbConnection conn)
    {
        var list = DupeSqlImporter.Import(conn).ToList();

        var progress = ImportProgressRegistry.Start(
            "DUPE SQL IMPORT",
            list.Count);

        try
        {
            foreach (var _ in list.TakeWhile(_ => !progress.CancelRequested)) progress.Processed++;
        }
        finally
        {
            ImportProgressRegistry.Finish();
        }

        return list;
    }
}