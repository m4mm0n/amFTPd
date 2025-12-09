/*
* ====================================================================================================
*  Project:        amFTPd - a managed FTP daemon
*  Author:         Geir Gustavsen, ZeroLinez Softworx
*  Created:        2025-11-25
*  Last Modified:  2025-12-01
*  
*  License:
*      MIT License
*      https://opensource.org/licenses/MIT
*
*  Notes:
* ====================================================================================================
*/

namespace amFTPd.Core.Site.Commands;

public sealed class SiteAddipCommand : SiteCommandBase
{
    public override string Name => "ADDIP";
    public override bool RequiresAdmin => true;
    public override string HelpText => "ADDIP <user> <ip-mask> [required-ident]";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE ADDIP <user> <ip-mask> [required-ident]\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await s.WriteAsync("501 Syntax: SITE ADDIP <user> <ip-mask> [required-ident]\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        var ipMask = parts[1];
        var ident = parts.Length >= 3 ? parts[2] : null;

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await s.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        user = user with
        {
            AllowedIpMask = ipMask,
            RequiredIdent = ident ?? user.RequiredIdent,
            RequireIdentMatch = !string.IsNullOrWhiteSpace(ident) || user.RequireIdentMatch
        };

        if (!context.Users.TryUpdateUser(user, out var error))
        {
            await s.WriteAsync($"550 Failed to update user: {error ?? "unknown error"}\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync("200 IP/IDENT updated.\r\n", cancellationToken);
    }
}