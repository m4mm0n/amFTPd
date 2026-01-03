using amFTPd.Core.Dupe;
using amFTPd.Core.Dupe.ImportExport;
using amFTPd.Db.Abstractions;
using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDupeMigrateCommand : SiteCommandBase
{
    public override string Name => "DUPEMIGRATE";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;

    public override string HelpText =>
        "DUPE MIGRATE [DRYRUN] SQL <provider> <conn> | DUPEFILE <path>";

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

        var idx = 0;
        var dryRun = false;

        if (idx < parts.Length &&
            parts[idx].Equals("DRYRUN", StringComparison.OrdinalIgnoreCase))
        {
            dryRun = true;
            idx++;
        }

        if (idx >= parts.Length)
        {
            await context.Session.WriteAsync(
                "501 Usage: DUPE MIGRATE [DRYRUN] SQL <provider> <conn> | DUPEFILE <path>\r\n",
                ct);
            return;
        }

        var mode = parts[idx++].ToUpperInvariant();

        var sb = new StringBuilder();
        sb.AppendLine($"200- DUPE MIGRATION {(dryRun ? "DRY-RUN" : "EXECUTE")}");

        var entries = store.ToSceneDupeDb();
        sb.AppendLine($" Entries: {entries.Count}");

        if (dryRun)
        {
            sb.AppendLine(" No changes applied.");
            sb.AppendLine("200 End.");
            await context.Session.WriteAsync(sb.ToString(), ct);
            return;
        }

        switch (mode)
        {
            case "SQL":
                {
                    if (idx + 1 >= parts.Length)
                    {
                        await context.Session.WriteAsync(
                            "501 Usage: DUPE MIGRATE SQL <provider> <connection-string>\r\n",
                            ct);
                        return;
                    }

                    var providerName = parts[idx++];
                    var connString = parts[idx];

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
                    sb.AppendLine($" Migrated to SQL ({providerName}).");
                    break;
                }

            case "DUPEFILE":
                {
                    if (idx >= parts.Length)
                    {
                        await context.Session.WriteAsync(
                            "501 Usage: DUPE MIGRATE DUPEFILE <path>\r\n",
                            ct);
                        return;
                    }

                    var path = parts[idx];
                    DupeFileExporter.Export(entries, path);
                    sb.AppendLine($" Written {path}");
                    break;
                }

            default:
                sb.AppendLine(" Unknown migration target.");
                break;
        }

        sb.AppendLine("200 Migration complete.");
        await context.Session.WriteAsync(sb.ToString(), ct);
    }
}