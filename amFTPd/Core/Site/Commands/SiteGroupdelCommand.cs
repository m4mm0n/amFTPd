/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteGroupdelCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 08:48:23
 *  Last Modified:  2025-12-14 21:30:44
 *  CRC32:          0xA5A5E956
 *  
 *  Description:
 *      SITE GROUPDEL removes an empty runtime group.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


namespace amFTPd.Core.Site.Commands;

/// <summary>
/// Implements <c>SITE GROUPDEL</c>, removing a group only when no user still belongs to it.
/// </summary>
public sealed class SiteGroupdelCommand : SiteCommandBase
{
    public override string Name => "GROUPDEL";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "GROUPDEL <group> - delete an empty scene group";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        if (context.Session.Account is not { IsAdmin: true })
        {
            await context.Session.WriteAsync("550 SITE GROUPDEL requires admin privileges.\r\n", cancellationToken);
            return;
        }

        var groupName = argument.Trim();
        if (string.IsNullOrWhiteSpace(groupName))
        {
            await context.Session.WriteAsync("501 Syntax: SITE GROUPDEL <group>\r\n", cancellationToken);
            return;
        }

        if (!context.Runtime.ContainsGroup(groupName))
        {
            await context.Session.WriteAsync("550 No such group.\r\n", cancellationToken);
            return;
        }

        var inUse = context.Users.GetAllUsers().Any(user =>
            string.Equals(user.PrimaryGroup, groupName, StringComparison.OrdinalIgnoreCase) ||
            user.SecondaryGroups.Any(group => string.Equals(group, groupName, StringComparison.OrdinalIgnoreCase)));

        if (inUse)
        {
            await context.Session.WriteAsync("550 Group is still assigned to one or more users.\r\n", cancellationToken);
            return;
        }

        if (context.Runtime.GroupStore is not null &&
            context.Runtime.GroupStore.FindGroup(groupName) is not null &&
            !context.Runtime.GroupStore.TryDeleteGroup(groupName, out var error))
        {
            await context.Session.WriteAsync($"550 Failed to delete group: {error ?? "unknown error"}\r\n", cancellationToken);
            return;
        }

        context.Runtime.RemoveGroup(groupName);
        context.Runtime.AuditLog?.Log(
            actor: context.Session.Account.UserName,
            action: "GROUPDEL",
            target: groupName,
            detail: string.Empty,
            ip: context.Session.RemoteEndPoint?.Address.ToString());

        await context.Session.WriteAsync($"200 Group deleted: {groupName}\r\n", cancellationToken);
    }
}
