/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteSetlimitsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-14 21:40:08
 *  CRC32:          0x74D24466
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

public sealed class SiteSetlimitsCommand : SiteCommandBase
{
    public override string Name => "SETLIMITS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "SETLIMITS <user> <up-kbps> <down-kbps> <maxlogins> <idle-seconds>";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE SETLIMITS <user> <up-kbps> <down-kbps> <maxlogins> <idle-seconds>\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
        {
            await s.WriteAsync("501 Syntax: SITE SETLIMITS <user> <up-kbps> <down-kbps> <maxlogins> <idle-seconds>\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        if (!int.TryParse(parts[1], out var up) ||
            !int.TryParse(parts[2], out var down) ||
            !int.TryParse(parts[3], out var maxLogins) ||
            !int.TryParse(parts[4], out var idleSec))
        {
            await s.WriteAsync("501 Invalid numeric argument.\r\n", cancellationToken);
            return;
        }

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await s.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        user = user with
        {
            MaxUploadKbps = up,
            MaxDownloadKbps = down,
            MaxConcurrentLogins = maxLogins,
            IdleTimeout = idleSec <= 0 ? null : TimeSpan.FromSeconds(idleSec)
        };

        if (!context.Users.TryUpdateUser(user, out var error))
        {
            await s.WriteAsync($"550 Failed to update limits: {error ?? "unknown error"}\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync("200 Limits updated.\r\n", cancellationToken);
    }
}