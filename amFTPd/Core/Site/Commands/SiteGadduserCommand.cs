/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteGadduserCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x26784AC9
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







namespace amFTPd.Core.Site.Commands;

public sealed class SiteGadduserCommand : SiteCommandBase
{
    public override string Name => "GADDUSER";
    public override bool RequiresAdmin => true;
    public override string HelpText => "GADDUSER";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {

        if (context.Session.Account is not { IsAdmin: true })
        {
            await context.Session.WriteAsync("550 SITE GADDUSER requires admin privileges.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync("501 Usage: SITE GADDUSER <group> <user>\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await context.Session.WriteAsync("501 Usage: SITE GADDUSER <group> <user>\r\n", cancellationToken);
            return;
        }

        var group = parts[0];
        var userName = parts[1];

        var existing = context.Session.Users.FindUser(userName);
        if (existing is null)
        {
            await context.Session.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        var updated = existing with { PrimaryGroup = group };

        if (context.Session.Users.TryUpdateUser(updated, out var error))
        {
            await context.Session.WriteAsync("200 User assigned to group.\r\n", cancellationToken);
        }
        else
        {
            await context.Session.WriteAsync($"550 Failed to update user: {error}\r\n", cancellationToken);
        }

    }
}