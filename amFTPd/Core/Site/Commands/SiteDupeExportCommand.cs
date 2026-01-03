using amFTPd.Core.Dupe;
using amFTPd.Core.Dupe.ImportExport;
using amFTPd.Db.Abstractions;
using System.Text.Json;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDupeExportCommand : SiteCommandBase
{
    public override string Name => "DUPEEXPORT";
    public override bool RequiresAdmin => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        var store = context.Runtime.DupeStore;
        if (store is null)
        {
            await context.Session.WriteAsync(
                "550 DUPE database not enabled.\r\n", ct);
            return;
        }

        var parts = argument.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 1)
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE DUPE EXPORT <dupefile|json|sql> [args]\r\n", ct);
            return;
        }

        var format = parts[0].ToLowerInvariant();
        var entries = store.ToSceneDupeDb();

        switch (format)
        {
            case "dupefile":
                {
                    var path = parts.Length > 1 ? parts[1] : "dupefile.dat";
                    DupeFileExporter.Export(entries, path);
                    await context.Session.WriteAsync(
                        $"200 DUPE exported to {path}\r\n", ct);
                    break;
                }

            case "json":
                {
                    var path = parts.Length > 1 ? parts[1] : "dupe.json";
                    File.WriteAllText(
                        path,
                        JsonSerializer.Serialize(
                            entries,
                            new JsonSerializerOptions { WriteIndented = true }));
                    await context.Session.WriteAsync(
                        $"200 DUPE exported to {path}\r\n", ct);
                    break;
                }

            case "sql":
                {
                    if (parts.Length < 3)
                    {
                        await context.Session.WriteAsync(
                            "501 Usage: SITE DUPE EXPORT SQL <provider> <connection-string>\r\n",
                            ct);
                        return;
                    }

                    var providerName = parts[1];
                    var connString = parts[2];

                    if (!SqlProviderRegistry.TryGet(providerName, out var provider))
                    {
                        var available = string.Join(", ", SqlProviderRegistry.Providers);
                        await context.Session.WriteAsync(
                            $"550 SQL provider '{providerName}' not available. " +
                            $"Available: {available}\r\n",
                            ct);
                        return;
                    }

                    await using var conn = provider.Create(connString);
                    DupeSqlExporter.Export(entries, conn);

                    await context.Session.WriteAsync(
                        $"200 DUPE exported to SQL ({providerName}).\r\n", ct);
                    break;
                }

            default:
                await context.Session.WriteAsync(
                    "550 Unknown export format.\r\n", ct);
                break;
        }
    }
}