/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteCreditsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x7A083ADF
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







using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteCreditsCommand : SiteCommandBase
{
    public override string Name => "CREDITS";
    public override string HelpText => "CREDITS [user] - show credits for you or another user";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        FtpUser? target;

        if (string.IsNullOrWhiteSpace(argument))
        {
            // No argument -> current user
            target = context.Session.Account;
            if (target is null)
            {
                await context.Session.WriteAsync(
                    "530 Not logged in.\r\n",
                    cancellationToken);
                return;
            }
        }
        else
        {
            // Admin can query arbitrary users
            var acc = context.Session.Account;
            if (acc is null || !acc.IsAdmin)
            {
                await context.Session.WriteAsync(
                    "550 Only admins can query other users' credits.\r\n",
                    cancellationToken);
                return;
            }

            var userName = argument.Trim();
            target = context.Users.FindUser(userName);
            if (target is null)
            {
                await context.Session.WriteAsync(
                    "550 No such user.\r\n",
                    cancellationToken);
                return;
            }
        }

        var line =
            $"200-User {target.UserName} credits:\r\n" +
            $"200 CREDITSKB={target.CreditsKb}\r\n";

        await context.Session.WriteAsync(line, cancellationToken);
    }
}