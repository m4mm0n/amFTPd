using System.Text;
using amFTPd.Core.Dupe;
using amFTPd.Core.Import;
using amFTPd.Core.Import.Mappers;
using amFTPd.Core.Import.Parsers;
using amFTPd.Core.Import.Records;
using amFTPd.Core.Sections;
using amFTPd.Utils.Tools;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteImportCommand : SiteCommandBase
{
    public override string Name => "IMPORT";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;

    public override string HelpText =>
        "IMPORT [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path>";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE IMPORT [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path>\r\n",
                cancellationToken);
            return;
        }

        var parts = argument.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        var idx = 0;
        var dryRun = false;

        if (parts[idx].Equals("DRYRUN", StringComparison.OrdinalIgnoreCase))
        {
            dryRun = true;
            idx++;
        }

        if (parts.Length - idx < 2)
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE IMPORT [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path>\r\n",
                cancellationToken);
            return;
        }

        ImportFlavor flavor;
        string rootPath;
        if (parts[idx].Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            idx++;
            if (parts.Length - idx < 1)
            {
                await context.Session.WriteAsync(
                    "501 Usage: SITE IMPORT [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path>\r\n",
                    cancellationToken);
                return;
            }

            rootPath = parts[idx++];
            flavor = ImportFlavorDetector.Detect(rootPath);
        }
        else if (ImportFlavorParser.TryParse(parts[idx], out var parsedFlavor))
        {
            flavor = parsedFlavor;
            idx++;
            rootPath = parts[idx];
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                await context.Session.WriteAsync(
                    "501 Usage: SITE IMPORT [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path>\r\n",
                    cancellationToken);
                return;
            }
        }
        else
        {
            await context.Session.WriteAsync(
                "501 Unknown import type. Use GLFTP|GLFTPD|IOFTPD|IO or AUTO.\r\n",
                cancellationToken);
            return;
        }

        if (!Directory.Exists(rootPath))
        {
            await context.Session.WriteAsync(
                "550 Import path does not exist.\r\n",
                cancellationToken);
            return;
        }

        if (flavor == ImportFlavor.Unknown)
        {
            await context.Session.WriteAsync(
                "550 Unable to auto-detect source type from path.\r\n",
                cancellationToken);
            return;
        }

        var result = await GlIoImport.ImportWithSummaryAsync(
            sourceRoot: rootPath,
            sections: context.Sections,
            db: context.Database,
            users: context.Users,
            groups: context.Groups,
            dryRun: dryRun,
            logger: context.Log,
            flavor: flavor,
            preRegistry: context.Runtime.PreRegistry,
            dupeStore: context.Runtime.DupeStore,
            zipscript: context.Runtime.Zipscript,
            cancellationToken: cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"200-IMPORT {(dryRun ? "DRY-RUN" : "EXECUTE")}");
        sb.AppendLine($" Source    : {result.Flavor} from {rootPath}");
        sb.AppendLine($" Groups    : Parsed {result.ParsedGroups}, Imported {result.ImportedGroups}");
        sb.AppendLine($" Users     : Parsed {result.ParsedUsers}, Imported {result.ImportedUsers}");
        sb.AppendLine($" PRE       : Parsed {result.ParsedPres}, Imported {result.ImportedPres}");
        sb.AppendLine($" Nukes     : Parsed {result.ParsedNukes}, Imported {result.ImportedNukes}");
        sb.AppendLine($" Dupes     : Parsed {result.ParsedDupes}, Added {result.ImportedDupes}, " +
            $"Updated {result.UpdatedDupes}, Skipped {result.SkippedDupes}, Nuked {result.NukedDupes}");
        if (result.UnknownSectionsCount > 0)
        {
            sb.AppendLine($" Unknown Sections ({result.UnknownSectionsCount}): " +
                string.Join(", ", result.UnknownSections));
        }

        sb.AppendLine("200 Import complete.");
        await context.Session.WriteAsync(sb.ToString(), cancellationToken);
    }
}

public sealed class SiteImportDupeCommand : SiteCommandBase
{
    public override string Name => "IMPORTDUPE";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;

    public override string HelpText =>
        "IMPORT DUPE [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path> [MERGE|OVERWRITE|SKIP]";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
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
                "501 Usage: SITE IMPORTDUPE [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path> [MERGE|OVERWRITE|SKIP]\r\n",
                ct);
            return;
        }

        ImportFlavor flavor;
        string rootPath;

        if (parts[idx].Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            idx++;
            if (idx >= parts.Length)
            {
                await context.Session.WriteAsync(
                    "501 Usage: SITE IMPORTDUPE [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path> [MERGE|OVERWRITE|SKIP]\r\n",
                    ct);
                return;
            }

            rootPath = parts[idx++];
            flavor = ImportFlavorDetector.Detect(rootPath);
        }
        else if (ImportFlavorParser.TryParse(parts[idx], out var parsed))
        {
            flavor = parsed;
            idx++;
            if (idx >= parts.Length)
            {
                await context.Session.WriteAsync(
                    "501 Usage: SITE IMPORTDUPE [DRYRUN] [AUTO|GLFTP|GLFTPD|IOFTPD|IO] <path> [MERGE|OVERWRITE|SKIP]\r\n",
                    ct);
                return;
            }

            rootPath = parts[idx++];
        }
        else
        {
            await context.Session.WriteAsync(
                "501 Unknown import type. Use GLFTP|GLFTPD|IOFTPD|IO or AUTO.\r\n",
                ct);
            return;
        }

        if (flavor == ImportFlavor.Unknown)
        {
            await context.Session.WriteAsync(
                "550 Unable to auto-detect FTPd type.\r\n",
                ct);
            return;
        }

        if (!Directory.Exists(rootPath))
        {
            await context.Session.WriteAsync(
                "550 Import path does not exist.\r\n",
                ct);
            return;
        }

        if (context.Runtime.DupeStore is null)
        {
            await context.Session.WriteAsync(
                "550 DUPE STORE not initialized.\r\n",
                ct);
            return;
        }

        var mode = DupeImportMode.Merge;
        if (idx < parts.Length &&
            Enum.TryParse(parts[idx], true, out DupeImportMode parsedMode))
        {
            mode = parsedMode;
        }

        IImportParser<ImportedDupeRecord> parser =
            flavor == ImportFlavor.GlFtpd
                ? new GlDupeParser()
                : new IoDupeParser();

        var records = parser.Parse(rootPath).ToList();
        var unknownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mappedRecords = new List<ImportedDupeRecord>(records.Count);

        foreach (var r in records)
        {
            var mappedSection = context.Sections.FindByNameOrAlias(r.Section);
            if (mappedSection is null)
            {
                unknownSections.Add(r.Section);
                continue;
            }

            mappedRecords.Add(new ImportedDupeRecord
            {
                Section = mappedSection.Name,
                Release = r.Release,
                Group = r.Group,
                FirstSeen = r.FirstSeen,
                TotalBytes = r.TotalBytes,
                IsNuked = r.IsNuked,
                NukeReason = r.NukeReason,
                NukeMultiplier = r.NukeMultiplier
            });
        }

        var stats = new DupeImportMapper()
            .Apply(mappedRecords, context.Runtime.DupeStore, mode, dryRun);

        var sb = new StringBuilder();
        sb.AppendLine($"200-IMPORT DUPE {(dryRun ? "DRY-RUN" : "EXECUTE")}");
        sb.AppendLine($" Source : {flavor}");
        sb.AppendLine($" Mode   : {mode}");
        sb.AppendLine($" Total  : {records.Count}");
        sb.AppendLine($" Mapped : {mappedRecords.Count}");
        sb.AppendLine($" Added  : {stats.Inserted}");
        sb.AppendLine($" Updated: {stats.Updated}");
        sb.AppendLine($" Skipped: {stats.Skipped}");
        sb.AppendLine($" Nuked  : {stats.Nuked}");
        if (unknownSections.Count > 0)
        {
            sb.AppendLine($" Unknown Sections ({unknownSections.Count}): {string.Join(", ", unknownSections.OrderBy(s => s))}");
        }
        sb.AppendLine("200 End.");

        await context.Session.WriteAsync(sb.ToString(), ct);
    }
}
