using amFTPd.Core.Dupe;
using amFTPd.Core.Dupe.ImportExport;
using amFTPd.Core.Import;
using System.Text.Json;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDupeImportCommand : SiteCommandBase
{
    public override string Name => "DUPEIMPORT";
    public override bool RequiresAdmin => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        var store = context.Runtime.DupeStore;
        if (store is null)
        {
            await context.Session.WriteAsync("550 DUPE database not enabled.\r\n", ct);
            return;
        }

        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE DUPE IMPORT <dupefile|json|sql> <path>\r\n", ct);
            return;
        }

        var format = parts[0].ToLowerInvariant();
        var path = parts[1];

        IReadOnlyList<SceneDupeEntry> entries;

        switch (format)
        {
            case "dupefile":
                entries = DupeFileImporter.Import(path);
                break;

            case "json":
                entries = JsonSerializer.Deserialize<List<SceneDupeEntry>>(
                    File.ReadAllText(path))!;
                break;

            default:
                await context.Session.WriteAsync("550 Unknown import format.\r\n", ct);
                return;
        }

        var progress = ImportProgressRegistry.Start(
            "DUPE IMPORT",
            entries.Count);

        try
        {
            foreach (var e in entries)
            {
                if (progress.CancelRequested)
                    break;

                context.Runtime.DupeStore!.Upsert(new DupeEntry
                {
                    SectionName = e.Section,
                    ReleaseName = e.ReleaseName,
                    TotalBytes = e.TotalBytes,
                    FirstSeen = e.ReleaseDate,
                    UploaderGroup = e.Group,
                    IsNuked = e.IsNuked,
                    NukeReason = e.NukeReason,
                    NukeMultiplier = (int)e.NukeMultiplier
                });

                progress.Processed++;
            }
        }
        finally
        {
            ImportProgressRegistry.Finish();
        }

        await context.Session.WriteAsync(
            $"200 Imported {entries.Count} dupe entries.\r\n", ct);
    }
}