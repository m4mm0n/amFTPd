/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteUnsuspendCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24
 *
 *  Description:
 *      SITE UNSUSPEND <user> — re-enable a suspended user account.
 *      Clears the Disabled flag set by SITE SUSPEND or SITE DELUSER.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using amFTPd.Logging;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteUnsuspendCommand : SiteCommandBase
{
    public override string Name => "UNSUSPEND";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "UNSUSPEND <user>  - re-enable a suspended user account.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE UNSUSPEND <user>\r\n", cancellationToken);
            return;
        }

        var userName = argument.Trim();
        if (string.IsNullOrWhiteSpace(userName))
        {
            await s.WriteAsync("501 Syntax: SITE UNSUSPEND <user>\r\n", cancellationToken);
            return;
        }

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await s.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        if (!user.Disabled)
        {
            await s.WriteAsync("200 User is already active.\r\n", cancellationToken);
            return;
        }

        user = user with { Disabled = false };

        if (!context.Users.TryUpdateUser(user, out var error))
        {
            await s.WriteAsync(
                $"550 Failed to unsuspend user: {error ?? "unknown error"}\r\n",
                cancellationToken);
            return;
        }

        var actor = s.Account?.UserName ?? "unknown";
        context.Log.Log(FtpLogLevel.Info, $"SITE UNSUSPEND {userName} by {actor}");

        context.Runtime.AuditLog?.Log(
            actor: actor,
            action: "UNSUSPEND",
            target: userName,
            ip: s.RemoteEndPoint?.Address.ToString());

        await s.WriteAsync(
            $"200 User {userName} unsuspended.\r\n",
            cancellationToken);
    }
}
