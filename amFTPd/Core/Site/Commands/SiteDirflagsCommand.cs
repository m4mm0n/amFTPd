using amFTPd.Config.Ftpd.RatioRules;
using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDirflagsCommand : SiteCommandBase
{
    public override string Name => "DIRFLAGS";
    public override bool RequiresAdmin => true;
    public override string HelpText => "DIRFLAGS [virt-path]  - show effective directory ratio/flags";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        var virt = string.IsNullOrWhiteSpace(argument)
            ? s.Cwd
            : FtpPath.Normalize(s.Cwd, argument);

        var sectionManager = context.Sections;
        var section = sectionManager.GetSectionForPath(virt);

        var dirRules = context.Runtime.DirectoryRules;
        var ratioRules = context.Runtime.RatioRules;

        // Find best matching DirectoryRule by prefix on key
        KeyValuePair<string, DirectoryRule>? dirMatch = null;
        foreach (var kv in dirRules.OrderByDescending(k => k.Key.Length))
        {
            if (virt.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                dirMatch = kv;
                break;
            }
        }

        KeyValuePair<string, RatioRule>? ratioMatch = null;
        foreach (var kv in ratioRules.OrderByDescending(k => k.Key.Length))
        {
            if (virt.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                ratioMatch = kv;
                break;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("200-Directory flags");
        sb.AppendLine($" Path      : {virt}");
        sb.AppendLine($" Section   : {section?.Name ?? "<none>"}");

        if (dirMatch is { } dm)
        {
            var r = dm.Value;
            sb.AppendLine($" DirRule   : key='{dm.Key}'");
            sb.AppendLine($"   AllowUp : {r.AllowUpload}");
            sb.AppendLine($"   AllowDn : {r.AllowDownload}");
            sb.AppendLine($"   AllowLs : {r.AllowList}");
            sb.AppendLine($"   IsFree  : {r.IsFree}");
            sb.AppendLine($"   Ratio   : {r.Ratio}");
            sb.AppendLine($"   MulCost : {r.MultiplyCost}");
            sb.AppendLine($"   UpBonus : {r.UploadBonus}");
        }
        else
        {
            sb.AppendLine(" DirRule   : <none>");
        }

        if (ratioMatch is { } rm)
        {
            var r = rm.Value;
            sb.AppendLine($" RatioRule : key='{rm.Key}'");
            sb.AppendLine($"   IsFree  : {r.IsFree}");
            sb.AppendLine($"   Ratio   : {r.Ratio}");
            sb.AppendLine($"   MulCost : {r.MultiplyCost}");
            sb.AppendLine($"   UpBonus : {r.UploadBonus}");
        }
        else
        {
            sb.AppendLine(" RatioRule : <none>");
        }

        sb.Append("200 End.\r\n");
        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}