/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteSuspendCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24
 *
 *  Description:
 *      SITE SUSPEND <user> [reason] — disable a user account without removing it.
 *      The account is preserved intact; the user simply cannot log in.
 *      Use SITE UNSUSPEND to re-enable.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using amFTPd.Logging;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteSuspendCommand : SiteCommandBase
{
    public override string Name => "SUSPEND";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "SUSPEND <user> [reason]  - disable a user account (reversible).";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE SUSPEND <user> [reason]\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 2, StringSplitOptions.TrimEntries);
        var userName = parts[0];
        var reason = parts.Length > 1 ? parts[1] : string.Empty;

        if (string.IsNullOrWhiteSpace(userName))
        {
            await s.WriteAsync("501 Syntax: SITE SUSPEND <user> [reason]\r\n", cancellationToken);
            return;
        }

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await s.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        if (user.Disabled)
        {
            await s.WriteAsync("200 User is already suspended.\r\n", cancellationToken);
            return;
        }

        user = user with { Disabled = true };

        if (!context.Users.TryUpdateUser(user, out var error))
        {
            await s.WriteAsync(
                $"550 Failed to suspend user: {error ?? "unknown error"}\r\n",
                cancellationToken);
            return;
        }

        var nuker = s.Account?.UserName ?? "unknown";
        var logMsg = string.IsNullOrWhiteSpace(reason)
            ? $"SITE SUSPEND {userName} by {nuker}"
            : $"SITE SUSPEND {userName} by {nuker} (reason: {reason})";

        context.Log.Log(FtpLogLevel.Info, logMsg);

        context.Runtime.AuditLog?.Log(
            actor: nuker,
            action: "SUSPEND",
            target: userName,
            detail: string.IsNullOrWhiteSpace(reason) ? null : $"reason={reason}",
            ip: s.RemoteEndPoint?.Address.ToString());

        await s.WriteAsync(
            $"200 User {userName} suspended.\r\n",
            cancellationToken);
    }
}
