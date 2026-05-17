/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteAuditlogCommand.cs
 *  Created:        2026-04-24
 *  Description:    SITE AUDITLOG — display the admin mutation audit log.
 *
 *  Usage:
 *    SITE AUDITLOG           -- last 20 entries
 *    SITE AUDITLOG <N>       -- last N entries (max 100)
 *    SITE AUDITLOG <user>    -- last 20 entries where actor or target matches <user>
 *
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using amFTPd.Core.Logging;

namespace amFTPd.Core.Site.Commands;

/// <summary>
/// <c>SITE AUDITLOG</c> — tail the admin mutation audit log.
/// Admin / siteop only.
/// </summary>
public sealed class SiteAuditlogCommand : SiteCommandBase
{
    public override string Name => "AUDITLOG";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "AUDITLOG [N|user]  - show last N audit entries (default 20) or filter by user";

    private const int DefaultCount = 20;
    private const int MaxCount = 100;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var runtime = context.Runtime;

        var auditLog = runtime.AuditLog;
        if (auditLog is null)
        {
            await s.WriteAsync("550 Audit log not initialised.\r\n", cancellationToken);
            return;
        }

        // Parse argument: optional int (count) or string (user filter)
        var arg = argument.Trim();
        int count = DefaultCount;
        string? userFilter = null;

        if (!string.IsNullOrEmpty(arg))
        {
            if (int.TryParse(arg, out var n))
                count = Math.Clamp(n, 1, MaxCount);
            else
                userFilter = arg;
        }

        // Pull lines — read more than requested when a user filter is active
        // so we have enough raw lines to fill the requested count after filtering.
        var fetchCount = userFilter is not null ? MaxCount : count;
        var lines = auditLog.ReadLastLines(fetchCount);

        // Apply optional user filter
        IEnumerable<string> filtered = lines;
        if (userFilter is not null)
        {
            filtered = lines.Where(l =>
            {
                var e = AuditLogWriter.ParseLine(l);
                if (e is null) return false;
                return string.Equals(e.Actor, userFilter, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.Target, userFilter, StringComparison.OrdinalIgnoreCase);
            });
        }

        var display = filtered.TakeLast(count).ToList();

        if (display.Count == 0)
        {
            await s.WriteAsync("200 No audit entries found.\r\n", cancellationToken);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("200-Audit log:");

        foreach (var line in display)
        {
            var e = AuditLogWriter.ParseLine(line);
            if (e is null)
            {
                sb.AppendLine($" [raw] {line}");
                continue;
            }

            // Human-readable one-liner:
            // 2026-04-24 12:00:00  n00bmk  ADDUSER  newuser  group=SITEOP  [192.168.1.1]
            var ts = e.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            var actor = e.Actor.PadRight(12);
            var action = e.Action.PadRight(12);
            var target = (e.Target ?? "-").PadRight(20);
            var detail = e.Detail ?? string.Empty;
            var ip = string.IsNullOrEmpty(e.Ip) ? string.Empty : $"  [{e.Ip}]";

            sb.AppendLine($" {ts}  {actor}  {action}  {target}  {detail}{ip}");
        }

        sb.Append($"200 {display.Count} audit entr{(display.Count == 1 ? "y" : "ies")} shown.");

        await s.WriteAsync(sb.ToString() + "\r\n", cancellationToken);
    }
}
