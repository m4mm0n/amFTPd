using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteGroupinfoCommand : SiteCommandBase
{
    public override string Name => "GROUPINFO";
    public override bool RequiresAdmin => true;
    public override string HelpText => "GROUPINFO <group> - show details for a group";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE GROUPINFO requires admin privileges.\r\n",
                cancellationToken);
            return;
        }

        var groupName = argument?.Trim();
        if (string.IsNullOrWhiteSpace(groupName))
        {
            await context.Session.WriteAsync(
                "501 Syntax: SITE GROUPINFO <group>\r\n",
                cancellationToken);
            return;
        }

        if (!context.Runtime.Groups.TryGetValue(groupName, out var cfg))
        {
            await context.Session.WriteAsync(
                "550 No such group.\r\n",
                cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("211-Group information:");
        sb.Append(" NAME=").AppendLine(groupName);
        sb.Append(" DESCRIPTION=").AppendLine(cfg.Description);
        sb.Append(" RATIO_MULTIPLY=").AppendLine(cfg.RatioMultiply.ToString());
        sb.Append(" UPLOAD_BONUS=").AppendLine(cfg.UploadBonus.ToString());
        sb.Append(" FLAGS=").AppendLine(cfg.Flags is { Count: > 0 } ? new string(cfg.Flags.ToArray()) : "-");
        sb.AppendLine("211 End");

        var text = sb.ToString().Replace("\n", "\r\n");
        await context.Session.WriteAsync(text, cancellationToken);
    }
}