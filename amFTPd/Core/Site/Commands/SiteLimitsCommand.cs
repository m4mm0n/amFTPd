/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteLimitsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 10:53:01
 *  Last Modified:  2025-12-14 21:32:35
 *  CRC32:          0x251D88B4
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


using System.Globalization;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteLimitsCommand : SiteCommandBase
{
    public override string Name => "LIMITS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "LIMITS <user> [ul_kbps dl_kbps max_logins [idle_seconds]] - show or set per-user limits";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE LIMITS requires admin/siteop privileges.\r\n",
                cancellationToken);
            return;
        }

        var parts = argument?
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? [];

        if (parts.Length == 0)
        {
            await context.Session.WriteAsync(
                "501 Syntax: SITE LIMITS <user> [ul_kbps dl_kbps max_logins [idle_seconds]]\r\n",
                cancellationToken);
            return;
        }

        var userName = parts[0];
        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await context.Session.WriteAsync(
                "550 No such user.\r\n",
                cancellationToken);
            return;
        }

        // Just show
        if (parts.Length == 1)
        {
            var idle = user.IdleTimeout.HasValue
                ? $"{(int)user.IdleTimeout.Value.TotalSeconds}s"
                : "default";

            var line =
                $"200-User {user.UserName} limits:\r\n" +
                $"200 UL_KBPS={user.MaxUploadKbps} DL_KBPS={user.MaxDownloadKbps} " +
                $"MAX_LOGINS={user.MaxConcurrentLogins} IDLE={idle}\r\n";

            await context.Session.WriteAsync(line, cancellationToken);
            return;
        }

        // Set mode
        if (parts.Length < 4)
        {
            await context.Session.WriteAsync(
                "501 Syntax: SITE LIMITS <user> <ul_kbps> <dl_kbps> <max_logins> [idle_seconds]\r\n",
                cancellationToken);
            return;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ul))
        {
            await context.Session.WriteAsync("501 Invalid ul_kbps.\r\n", cancellationToken);
            return;
        }

        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dl))
        {
            await context.Session.WriteAsync("501 Invalid dl_kbps.\r\n", cancellationToken);
            return;
        }

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLogins))
        {
            await context.Session.WriteAsync("501 Invalid max_logins.\r\n", cancellationToken);
            return;
        }

        var idleTimeout = user.IdleTimeout;
        if (parts.Length >= 5)
        {
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idleSeconds))
            {
                await context.Session.WriteAsync("501 Invalid idle_seconds.\r\n", cancellationToken);
                return;
            }

            idleTimeout = idleSeconds <= 0 ? null : TimeSpan.FromSeconds(idleSeconds);
        }

        var updated = user with
        {
            MaxUploadKbps = ul,
            MaxDownloadKbps = dl,
            MaxConcurrentLogins = maxLogins,
            IdleTimeout = idleTimeout
        };

        if (!context.Users.TryUpdateUser(updated, out var error))
        {
            await context.Session.WriteAsync(
                $"550 Failed to update user: {error ?? "unknown error"}\r\n",
                cancellationToken);
            return;
        }

        var idleText = updated.IdleTimeout.HasValue
            ? $"{(int)updated.IdleTimeout.Value.TotalSeconds}s"
            : "default";

        await context.Session.WriteAsync(
            $"200 Limits for {updated.UserName} updated: UL={updated.MaxUploadKbps} DL={updated.MaxDownloadKbps} " +
            $"MAX_LOGINS={updated.MaxConcurrentLogins} IDLE={idleText}\r\n",
            cancellationToken);
    }
}