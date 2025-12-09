using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteGroupmembersCommand : SiteCommandBase
{
    public override string Name => "GROUPMEMBERS";
    public override bool RequiresAdmin => true;
    public override string HelpText => "GROUPMEMBERS <group>  - list users in group";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE GROUPMEMBERS <group>\r\n", cancellationToken);
            return;
        }

        var group = argument.Trim();
        var allUsers = context.Users.GetAllUsers();

        var members = allUsers
            .Where(u => u.PrimaryGroup.Equals(group, StringComparison.OrdinalIgnoreCase) ||
                        u.SecondaryGroups.Any(g => g.Equals(group, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(u => u.UserName)
            .ToList();

        if (members.Count == 0)
        {
            await s.WriteAsync("200 No members found.\r\n", cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"200-Members of {group}:");
        foreach (var u in members)
            sb.AppendLine($" {u.UserName}");
        sb.Append("200 End.\r\n");

        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}