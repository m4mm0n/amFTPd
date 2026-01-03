using amFTPd.Core.Import;
using amFTPd.Core.Import.Mappers;
using amFTPd.Core.Import.Parsers;
using amFTPd.Core.Import.Records;
using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteImportCommand : SiteCommandBase
{
    public override string Name => "IMPORT";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;

    public override string HelpText =>
        "IMPORT [DRYRUN] GLFTP|IOFTPD <path>";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE IMPORT [DRYRUN] GLFTP|IOFTPD <path>\r\n",
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
                "501 Usage: SITE IMPORT [DRYRUN] GLFTP|IOFTPD <path>\r\n",
                cancellationToken);
            return;
        }

        var mode = parts[idx++].ToUpperInvariant();
        var rootPath = parts[idx];

        if (!Directory.Exists(rootPath))
        {
            await context.Session.WriteAsync(
                "550 Import path does not exist.\r\n",
                cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"200-IMPORT {(dryRun ? "DRY-RUN" : "EXECUTE")}");

        // -------------------------------
        // Select parsers
        // -------------------------------
        var isGl = mode == "GLFTP";
        var isIo = mode == "IOFTPD";

        if (!isGl && !isIo)
        {
            await context.Session.WriteAsync(
                "501 Unknown import type. Use GLFTP or IOFTPD.\r\n",
                cancellationToken);
            return;
        }

        // -------------------------------
        // GROUPS
        // -------------------------------
        IImportParser<ImportedGroupRecord> groupParser = isGl
            ? new GlGroupParser()
            : new IoGroupParser(); // future-proof

        var groupRecords = groupParser.Parse(rootPath).ToList();
        sb.AppendLine($" Groups: {groupRecords.Count}");

        if (!dryRun)
        {
            new GroupImportMapper()
                .Apply(groupRecords, context.Groups);
        }

        // -------------------------------
        // USERS
        // -------------------------------
        IImportParser<ImportedUserRecord> userParser = isGl
            ? new GlUserParser()
            : new IoUserParser(); // future-proof

        var userRecords = userParser.Parse(rootPath).ToList();
        sb.AppendLine($" Users: {userRecords.Count}");

        if (!dryRun)
        {
            new UserImportMapper()
                .Apply(userRecords, context.Users);

            new GroupMembershipReconciler()
                .Apply(context.Users, context.Groups);
        }

        // -------------------------------
        // PRE
        // -------------------------------
        IImportParser<ImportedPreRecord> preParser = isGl
            ? new GlPreParser()
            : new IoPreParser();

        var preRecords = preParser.Parse(rootPath).ToList();
        sb.AppendLine($" PREs: {preRecords.Count}");

        if (!dryRun)
        {
            new PreImportMapper()
                .Apply(preRecords, context.Runtime.PreRegistry);
        }

        // -------------------------------
        // NUKE
        // -------------------------------
        IImportParser<ImportedNukeRecord> nukeParser = isGl
            ? new GlNukeParser()
            : new IoNukeParser();

        var nukeRecords = nukeParser.Parse(rootPath).ToList();
        sb.AppendLine($" Nukes: {nukeRecords.Count}");

        if (!dryRun && context.Runtime.Zipscript is not null)
        {
            new NukeImportMapper(context.Log)
                .Apply(nukeRecords, context.Runtime.Zipscript);
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
        "IMPORT DUPE [DRYRUN] [AUTO|GLFTP|IOFTPD] <path> [MERGE|OVERWRITE|SKIP]";

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

        ImportFlavor flavor;
        if (idx < parts.Length &&
            Enum.TryParse(parts[idx], true, out ImportFlavor parsed))
        {
            flavor = parsed;
            idx++;
        }
        else
        {
            flavor = ImportFlavorDetector.Detect(parts[idx]);
        }

        if (flavor == ImportFlavor.Unknown)
        {
            await context.Session.WriteAsync(
                "550 Unable to auto-detect FTPd type.\r\n", ct);
            return;
        }

        var rootPath = parts[idx++];
        if (!Directory.Exists(rootPath))
        {
            await context.Session.WriteAsync(
                "550 Import path does not exist.\r\n", ct);
            return;
        }

        var mode = DupeImportMode.Merge;
        if (idx < parts.Length &&
            Enum.TryParse(parts[idx], true, out DupeImportMode parsedMode))
            mode = parsedMode;

        IImportParser<ImportedDupeRecord> parser =
            flavor == ImportFlavor.GlFtpd
                ? new GlDupeParser()
                : new IoDupeParser();

        var records = parser.Parse(rootPath).ToList();

        var stats = new DupeImportMapper()
            .Apply(records, context.Runtime.DupeStore!, mode, dryRun);

        var sb = new StringBuilder();
        sb.AppendLine($"200-IMPORT DUPE {(dryRun ? "DRY-RUN" : "EXECUTE")}");
        sb.AppendLine($" Source : {flavor}");
        sb.AppendLine($" Mode   : {mode}");
        sb.AppendLine($" Total  : {stats.Total}");
        sb.AppendLine($" Added  : {stats.Inserted}");
        sb.AppendLine($" Updated: {stats.Updated}");
        sb.AppendLine($" Skipped: {stats.Skipped}");
        sb.AppendLine($" Nuked  : {stats.Nuked}");
        sb.AppendLine("200 End.");

        await context.Session.WriteAsync(sb.ToString(), ct);
    }
}