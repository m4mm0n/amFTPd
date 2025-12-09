/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteReqidentCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:33
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xB0AE3012
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

public sealed class SiteReqidentCommand : SiteCommandBase
{
    public override string Name => "REQIDENT";
    public override bool RequiresAdmin => true;
    public override string HelpText => "REQIDENT <user> <on|off> [ident]";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE REQIDENT <user> <on|off> [ident]\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await s.WriteAsync("501 Syntax: SITE REQIDENT <user> <on|off> [ident]\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        var mode = parts[1].ToLowerInvariant();
        var ident = parts.Length == 3 ? parts[2] : null;

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await s.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        var enable = mode is "on" or "1" or "true" or "yes";

        user = user with
        {
            RequireIdentMatch = enable,
            RequiredIdent = ident ?? user.RequiredIdent
        };

        if (!context.Users.TryUpdateUser(user, out var error))
        {
            await s.WriteAsync($"550 Failed to update user: {error ?? "unknown error"}\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync("200 IDENT requirement updated.\r\n", cancellationToken);
    }
}