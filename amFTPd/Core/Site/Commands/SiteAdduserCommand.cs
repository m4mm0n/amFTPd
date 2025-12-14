/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteAdduserCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-14 21:27:10
 *  CRC32:          0xDE221B86
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


using amFTPd.Config.Ftpd;
using amFTPd.Security;
using System.Collections.Immutable;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteAdduserCommand : SiteCommandBase
{
    public override string Name => "ADDUSER";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "ADDUSER <user> <password> [group] [homedir]";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE ADDUSER <user> <password> [group] [homedir]\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await s.WriteAsync("501 Syntax: SITE ADDUSER <user> <password> [group] [homedir]\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        var password = parts[1];
        var group = parts.Length >= 3 ? parts[2] : "users";
        var home = parts.Length >= 4 ? parts[3] : $"/{userName}";

        if (context.Users.FindUser(userName) is not null)
        {
            await s.WriteAsync("550 User already exists.\r\n", cancellationToken);
            return;
        }

        var pwHash = PasswordHasher.HashPassword(password);

        var user = new FtpUser(
            UserName: userName,
            PasswordHash: pwHash,
            Disabled: false,
            HomeDir: home,
            PrimaryGroup: group,
            SecondaryGroups: ImmutableArray<string>.Empty,
            IsAdmin: false,
            AllowFxp: false,
            AllowUpload: true,
            AllowDownload: true,
            AllowActiveMode: true,
            RequireIdentMatch: false,
            AllowedIpMask: string.Empty,
            RequiredIdent: string.Empty,
            IdleTimeout: null,
            MaxUploadKbps: 0,
            MaxDownloadKbps: 0,
            CreditsKb: 0,
            Sections: context.Sections.GetSections(), // give access to all current sections
            MaxConcurrentLogins: 0,
            IsNoRatio: false,
            FlagsRaw: string.Empty
        );

        if (!context.Users.TryAddUser(user, out var error))
        {
            await s.WriteAsync($"550 Failed to add user: {error ?? "unknown error"}\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync($"200 User {userName} created.\r\n", cancellationToken);
    }
}