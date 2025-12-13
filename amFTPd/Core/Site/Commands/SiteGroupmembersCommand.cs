/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteGroupmembersCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 09:46:26
 *  Last Modified:  2025-12-13 04:45:42
 *  CRC32:          0xB57904E3
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */









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
            .Where(u => u.PrimaryGroup != null && (u.PrimaryGroup.Equals(group, StringComparison.OrdinalIgnoreCase) ||
                                                   u.SecondaryGroups.Any(g => g != null && g.Equals(group, StringComparison.OrdinalIgnoreCase))))
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