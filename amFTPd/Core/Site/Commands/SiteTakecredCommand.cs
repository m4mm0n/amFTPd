/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteTakecredCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-13 04:32:32
 *  CRC32:          0x6C8020F7
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









using System.Globalization;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteTakecredCommand : SiteCommandBase
{
    public override string Name => "TAKECRED";
    public override bool RequiresAdmin => true;
    public override string HelpText => "TAKECRED <user> <kb> - remove credits (KiB) from a user";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE TAKECRED requires admin privileges.\r\n",
                cancellationToken);
            return;
        }

        var parts = argument?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? [];

        if (parts.Length != 2)
        {
            await context.Session.WriteAsync(
                "501 Syntax: SITE TAKECRED <user> <kb>\r\n",
                cancellationToken);
            return;
        }

        var userName = parts[0];
        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var deltaKb))
        {
            await context.Session.WriteAsync(
                "501 Invalid credit amount.\r\n",
                cancellationToken);
            return;
        }

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await context.Session.WriteAsync(
                "550 No such user.\r\n",
                cancellationToken);
            return;
        }

        var newCredits = user.CreditsKb - deltaKb;
        if (newCredits < 0) newCredits = 0;

        var updated = user with { CreditsKb = newCredits };
        if (!context.Users.TryUpdateUser(updated, out var error))
        {
            await context.Session.WriteAsync(
                $"550 Failed to update user: {error ?? "unknown error"}\r\n",
                cancellationToken);
            return;
        }

        await context.Session.WriteAsync(
            $"200 {deltaKb} KiB removed from {updated.UserName}. New balance: {updated.CreditsKb} KiB\r\n",
            cancellationToken);
    }
}