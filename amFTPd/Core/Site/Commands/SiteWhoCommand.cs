/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteWhoCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-10 03:58:32
 *  CRC32:          0xC6978A93
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

public sealed class SiteWhoCommand : SiteCommandBase
{
    public override string Name => "WHO";
    public override string HelpText => "WHO - list active sessions";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var bus = context.Runtime.EventBus;

        if (bus is null)
        {
            await s.WriteAsync("550 No session registry available.\r\n", cancellationToken);
            return;
        }

        var sessions = bus.GetActiveSessions().OrderBy(sess => sess.Account?.UserName ?? "").ToList();

        if (sessions.Count == 0)
        {
            await s.WriteAsync("200 No active sessions.\r\n", cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("200-Active sessions:");
        foreach (var sess in sessions)
        {
            var acc = sess.Account;
            var user = acc?.UserName ?? "<unknown>";
            var host = sess.Control.Client.RemoteEndPoint?.ToString() ?? "<unknown>";
            var cwd = sess.Cwd;

            sb.Append(" ");
            sb.Append(user.PadRight(12));
            sb.Append(" ");
            sb.Append(host.PadRight(22));
            sb.Append(" ");
            sb.Append(cwd);
            sb.AppendLine();
        }

        sb.Append("200 End.\r\n");
        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}