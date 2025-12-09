using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteGroupsCommand : SiteCommandBase
{
    public override string Name => "GROUPS";
    public override bool RequiresAdmin => true;
    public override string HelpText => "GROUPS - list all configured groups";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE GROUPS requires admin privileges.\r\n",
                cancellationToken);
            return;
        }

        var groups = context.Runtime.Groups
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("211-Configured groups:");

        foreach (var (name, cfg) in groups)
        {
            sb.Append(" NAME=").Append(name);
            sb.Append(" DESC=").Append(cfg.Description);
            sb.Append(" RATIO_MUL=").Append(cfg.RatioMultiply);
            sb.Append(" UL_BONUS=").Append(cfg.UploadBonus);
            sb.Append(" FLAGS=").Append(cfg.Flags is { Count: > 0 } ? new string(cfg.Flags.ToArray()) : "-");
            sb.AppendLine();
        }

        sb.AppendLine("211 End");

        var text = sb.ToString().Replace("\n", "\r\n");
        await context.Session.WriteAsync(text, cancellationToken);
    }
}