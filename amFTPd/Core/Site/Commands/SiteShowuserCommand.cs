/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteShowuserCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 02:56:04
 *  Last Modified:  2025-12-10 03:58:32
 *  CRC32:          0xF43CF8BA
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

namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteShowuserCommand : SiteCommandBase
    {
        public override string Name => "SHOWUSER";
        public override bool RequiresAdmin => true;
        public override string HelpText => "SHOWUSER <user> - show detailed info about a user";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var acc = context.Session.Account;
            if (acc is not { IsAdmin: true })
            {
                await context.Session.WriteAsync(
                    "550 SITE SHOWUSER requires admin privileges.\r\n",
                    cancellationToken);
                return;
            }

            var userName = argument?.Trim();
            if (string.IsNullOrWhiteSpace(userName))
            {
                await context.Session.WriteAsync(
                    "501 Syntax: SITE SHOWUSER <user>\r\n",
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

            var sb = new StringBuilder();
            sb.AppendLine("211-User information:");
            sb.Append(" USER=").AppendLine(user.UserName);
            sb.Append(" DISABLED=").AppendLine(user.Disabled ? "Y" : "N");
            sb.Append(" HOME=").AppendLine(user.HomeDir);
            sb.Append(" GROUP=").AppendLine(user.PrimaryGroup);
            sb.Append(" SECONDARY=").AppendLine(
                user.SecondaryGroups is { Count: > 0 }
                    ? string.Join(",", user.SecondaryGroups)
                    : "-");
            sb.Append(" ADMIN=").AppendLine(user.IsAdmin ? "Y" : "N");
            sb.Append(" FXP=").AppendLine(user.AllowFxp ? "Y" : "N");
            sb.Append(" UL=").AppendLine(user.AllowUpload ? "Y" : "N");
            sb.Append(" DL=").AppendLine(user.AllowDownload ? "Y" : "N");
            sb.Append(" ACT=").AppendLine(user.AllowActiveMode ? "Y" : "N");
            sb.Append(" REQ_IDENT=").AppendLine(user.RequireIdentMatch ? "Y" : "N");
            sb.Append(" ALLOWED_IP=")
                .AppendLine(string.IsNullOrWhiteSpace(user.AllowedIpMask) ? "-" : user.AllowedIpMask);
            sb.Append(" REQ_IDENT_NAME=")
                .AppendLine(string.IsNullOrWhiteSpace(user.RequiredIdent) ? "-" : user.RequiredIdent);
            sb.Append(" IDLE_TIMEOUT=").AppendLine(
                user.IdleTimeout.HasValue ? $"{(int)user.IdleTimeout.Value.TotalSeconds}s" : "default");
            sb.Append(" MAX_UL_KBPS=").AppendLine(user.MaxUploadKbps.ToString());
            sb.Append(" MAX_DL_KBPS=").AppendLine(user.MaxDownloadKbps.ToString());
            sb.Append(" MAX_LOGINS=").AppendLine(user.MaxConcurrentLogins.ToString());
            sb.Append(" CREDITSKB=").AppendLine(user.CreditsKb.ToString());
            sb.Append(" NORATIO=").AppendLine(user.IsNoRatio ? "Y" : "N");
            sb.Append(" FLAGS=").AppendLine(string.IsNullOrWhiteSpace(user.FlagsRaw) ? "-" : user.FlagsRaw);

            sb.AppendLine("211 End");

            var text = sb.ToString().Replace("\n", "\r\n");
            await context.Session.WriteAsync(text, cancellationToken);
        }

    }
}
