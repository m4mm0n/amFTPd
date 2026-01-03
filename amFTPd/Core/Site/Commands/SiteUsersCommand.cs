/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteUsersCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-14 21:42:48
 *  CRC32:          0x862A713A
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


using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteUsersCommand : SiteCommandBase
{
    public override string Name => "USERS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "USERS - list configured users";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE USERS requires admin privileges.\r\n",
                cancellationToken);
            return;
        }

        var full = argument?.Trim()
            .Equals("FULL", StringComparison.OrdinalIgnoreCase) == true;

        var users = context.Users.GetAllUsers()
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var live = context.Runtime.LiveStats;

        var sb = new StringBuilder();
        sb.AppendLine(full
            ? "211-User statistics (live):"
            : "211-Configured users:");

        foreach (var u in users)
        {
            sb.Append(" USER=").Append(u.UserName);
            sb.Append(" GROUP=").Append(u.PrimaryGroup);
            sb.Append(" ADMIN=").Append(u.IsAdmin ? "Y" : "N");
            sb.Append(" FXP=").Append(u.AllowFxp ? "Y" : "N");
            sb.Append(" UL=").Append(u.AllowUpload ? "Y" : "N");
            sb.Append(" DL=").Append(u.AllowDownload ? "Y" : "N");
            sb.Append(" ACT=").Append(u.AllowActiveMode ? "Y" : "N");
            sb.Append(" NORATIO=").Append(u.IsNoRatio ? "Y" : "N");
            sb.Append(" CREDITSKB=").Append(u.CreditsKb);

            if (full &&
                live.Users.TryGetValue(u.UserName, out var lu))
            {
                sb.Append(" UL#=").Append(lu.Uploads);
                sb.Append(" DL#=").Append(lu.Downloads);
                sb.Append(" UP=").Append(lu.BytesUploaded);
                sb.Append(" DN=").Append(lu.BytesDownloaded);
                sb.Append(" SESS=").Append(lu.ActiveSessions);
            }

            sb.AppendLine();
        }

        sb.AppendLine("211 End");

        await context.Session.WriteAsync(
            sb.ToString().Replace("\n", "\r\n"),
            cancellationToken);
    }
}