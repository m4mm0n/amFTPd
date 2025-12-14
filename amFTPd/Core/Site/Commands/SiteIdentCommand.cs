/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteIdentCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:33
 *  Last Modified:  2025-12-14 21:31:35
 *  CRC32:          0x2ED7FE55
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
 * ====================================================================================================
 */


namespace amFTPd.Core.Site.Commands;

public sealed class SiteIdentCommand : SiteCommandBase
{
    public override string Name => "IDENT";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "IDENT <user> <ident> - set required IDENT for user";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {

        if (context.Session.Account is not { IsAdmin: true })
        {
            await context.Session.WriteAsync("550 Permission denied.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync("501 Syntax: SITE IDENT <user> <ident>\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await context.Session.WriteAsync("501 Syntax: SITE IDENT <user> <ident>\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        var ident = parts[1];

        var user = context.Session.Users.FindUser(userName);
        if (user is null)
        {
            await context.Session.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        var updated = user with { RequiredIdent = ident };

        if (context.Session.Users.TryUpdateUser(updated, out var error))
        {
            await context.Session.WriteAsync($"200 Required IDENT for '{userName}' set to '{ident}'.\r\n", cancellationToken);
        }
        else
        {
            await context.Session.WriteAsync($"550 Failed to update user: {error}\r\n", cancellationToken);
        }
    }
}