/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteLinesCommand.cs
 *  Created:        2026-04-24
 *  Description:    SITE LINES [count] — display the last N oneliners.
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteLinesCommand : SiteCommandBase
{
    public override string Name => "LINES";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "LINES [count]  - display the last N oneliners (default 10).";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var store = context.Runtime.OnelineStore;

        if (store is null)
        {
            await s.WriteAsync("550 Oneliner store is not enabled.\r\n", cancellationToken);
            return;
        }

        int count = 10;
        if (!string.IsNullOrWhiteSpace(argument) &&
            int.TryParse(argument.Trim(), out var parsed) && parsed > 0)
            count = Math.Min(parsed, 100);

        var lines = store.GetRecent(count);

        if (lines.Count == 0)
        {
            await s.WriteAsync("200 No oneliners yet.\r\n", cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"200-Last {lines.Count} oneliner(s):");

        foreach (var e in lines)
        {
            var when = e.PostedAt.ToLocalTime().ToString("MM-dd HH:mm");
            var who = string.IsNullOrEmpty(e.GroupName)
                ? e.UserName
                : $"{e.UserName}/{e.GroupName}";

            sb.AppendLine($" [{e.Id:D4}] {when} <{who}> {e.Message}");
        }

        sb.Append("200 End.\r\n");
        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}
