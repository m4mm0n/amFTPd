/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteWhoCommand.cs
 *  Created:        2026-04-24
 *  Description:    SITE WHO — scene-standard active-session display.
 *                  Shows user, group, direction (UP/DN/IDLE), filename, speed, and % complete.
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteWhoCommand : SiteCommandBase
{
    public override string Name => "WHO";
    public override string HelpText => "WHO  - list active sessions with transfer state.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var sessions = FtpSession.GetActiveSessions();

        int uploading = 0, downloading = 0;

        var sb = new StringBuilder();
        sb.AppendLine("200-=[ WHO ]==============================================================");
        sb.AppendLine("  #  User         Group        Status  File                       Speed    %");
        sb.AppendLine("  -  -----------  -----------  ------  -------------------------  -------  ---");

        int slot = 0;
        foreach (var sess in sessions.OrderBy(x => x.SessionId))
        {
            slot++;
            var acc = sess.Account;
            var user = (acc?.UserName ?? "<unknown>").PadRight(11);
            var group = (acc?.GroupName ?? "-").PadRight(11);

            var dir = sess.TransferDirection;   // 0=idle, 1=up, 2=dn
            var fname = sess.TransferFileName;
            var started = sess.TransferStartedAt;
            var bytes = Volatile.Read(ref sess.TransferBytesCompleted);
            var total = Volatile.Read(ref sess.TransferTotalBytes);

            string status;
            string fileDisplay;
            string speedStr;
            string pctStr;

            if (dir == 1)          // Upload
            {
                uploading++;
                status = "UP    ";
                fileDisplay = TruncateLeft(fname ?? "?", 25);
                var kbps = ComputeKbps(bytes, started);
                speedStr = $"{kbps,6:F0} KB";
                pctStr = total > 0 ? $"{bytes * 100 / total,3}%" : "  -%";
            }
            else if (dir == 2)     // Download
            {
                downloading++;
                status = "DN    ";
                fileDisplay = TruncateLeft(fname ?? "?", 25);
                var kbps = ComputeKbps(bytes, started);
                speedStr = $"{kbps,6:F0} KB";
                pctStr = total > 0 ? $"{bytes * 100 / total,3}%" : "  -%";
            }
            else                   // Idle
            {
                status = "IDLE  ";
                fileDisplay = TruncateLeft(sess.Cwd, 25);
                speedStr = "      0 KB";
                pctStr = "   ";
            }

            sb.AppendLine(
                $"  {slot,-3}{user}  {group}  {status}  {fileDisplay,-25}  {speedStr}  {pctStr}");
        }

        if (slot == 0)
        {
            await s.WriteAsync("200 No active sessions.\r\n", cancellationToken);
            return;
        }

        sb.AppendLine("  -----------------------------------------------------------------------");
        sb.Append(
            $"200 {slot} user{(slot == 1 ? "" : "s")} online" +
            $" — {uploading} uploading, {downloading} downloading.\r\n");

        await s.WriteAsync(sb.ToString(), cancellationToken);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static double ComputeKbps(long bytes, DateTimeOffset started)
    {
        var elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;
        if (elapsed < 0.1) return 0;
        return (bytes / 1024.0) / elapsed;
    }

    private static string TruncateLeft(string s, int maxLen)
    {
        if (s.Length <= maxLen) return s;
        return "…" + s[^(maxLen - 1)..];
    }
}
