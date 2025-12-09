/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteUsersCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x6F438EC0
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







using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteUsersCommand : SiteCommandBase
{
    public override string Name => "USERS";
    public override bool RequiresAdmin => true;
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

        var users = context.Users.GetAllUsers()
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("211-Configured users:");

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
            sb.AppendLine();
        }

        sb.AppendLine("211 End");

        var text = sb.ToString().Replace("\n", "\r\n");
        await context.Session.WriteAsync(text, cancellationToken);
    }
}