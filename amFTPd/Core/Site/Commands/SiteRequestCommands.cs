/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRequestCommands.cs
 *  Created:        2026-04-24
 *  Description:    SITE REQUEST / REQFILLED / REQLIST / REQDEL / REQWIPE
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text;
using amFTPd.Core.Events;

namespace amFTPd.Core.Site.Commands;

// ---------------------------------------------------------------------------
// SITE REQUEST <releasename>
// ---------------------------------------------------------------------------
public sealed class SiteRequestCommand : SiteCommandBase
{
    public override string Name => "REQUEST";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "REQUEST <releasename>  - post a release request.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var reg = context.Runtime.RequestRegistry;

        if (reg is null)
        {
            await s.WriteAsync("550 Request registry not available.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE REQUEST <releasename>\r\n", cancellationToken);
            return;
        }

        var relName = argument.Trim();
        var user = s.Account?.UserName ?? "unknown";
        var group = s.Account?.GroupName;

        var entry = reg.Add(relName, user, group);
        if (entry is null)
        {
            await s.WriteAsync(
                $"550 Request for '{relName}' already exists.\r\n",
                cancellationToken);
            return;
        }

        context.Runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.Request,
            Timestamp = entry.RequestedAt,
            SessionId = s.SessionId,
            User = user,
            Group = group,
            ReleaseName = relName,
            Extra = $"id={entry.Id} action=new"
        });

        await s.WriteAsync(
            $"200 Request #{entry.Id} posted: {relName}\r\n",
            cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE REQFILLED <releasename>
// ---------------------------------------------------------------------------
public sealed class SiteReqfilledCommand : SiteCommandBase
{
    public override string Name => "REQFILLED";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "REQFILLED <releasename>  - mark a request as filled.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var reg = context.Runtime.RequestRegistry;

        if (reg is null)
        {
            await s.WriteAsync("550 Request registry not available.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE REQFILLED <releasename>\r\n", cancellationToken);
            return;
        }

        var relName = argument.Trim();
        var filler = s.Account?.UserName ?? "unknown";

        var entry = reg.MarkFilled(relName, filler);
        if (entry is null)
        {
            await s.WriteAsync(
                $"550 No open request found for '{relName}'.\r\n",
                cancellationToken);
            return;
        }

        context.Runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.Request,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = s.SessionId,
            User = filler,
            ReleaseName = relName,
            Extra = $"id={entry.Id} action=filled requestedBy={entry.RequestedBy}"
        });

        await s.WriteAsync(
            $"200 Request '{relName}' marked as filled by {filler}.\r\n",
            cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE REQLIST [filter]
// ---------------------------------------------------------------------------
public sealed class SiteReqlistCommand : SiteCommandBase
{
    public override string Name => "REQLIST";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "REQLIST [filter]  - list open requests (optional keyword filter).";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var reg = context.Runtime.RequestRegistry;

        if (reg is null)
        {
            await s.WriteAsync("550 Request registry not available.\r\n", cancellationToken);
            return;
        }

        var filter = string.IsNullOrWhiteSpace(argument) ? null : argument.Trim();
        var reqs = reg.GetOpen(filter);

        if (reqs.Count == 0)
        {
            var msg = filter is not null
                ? $"200 No open requests matching '{filter}'.\r\n"
                : "200 No open requests.\r\n";
            await s.WriteAsync(msg, cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"200-Open requests ({reqs.Count}):");
        foreach (var r in reqs)
        {
            var age = (DateTimeOffset.UtcNow - r.RequestedAt).TotalDays;
            sb.AppendLine(
                $" [{r.Id:D4}] {r.ReleaseName,-45} by {r.RequestedBy,-12} ({age:F0}d ago)");
        }
        sb.Append("200 End.\r\n");
        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE REQDEL <id>
// ---------------------------------------------------------------------------
public sealed class SiteReqdelCommand : SiteCommandBase
{
    public override string Name => "REQDEL";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "REQDEL <id>  - delete a request by ID.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var reg = context.Runtime.RequestRegistry;

        if (reg is null)
        {
            await s.WriteAsync("550 Request registry not available.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument) ||
            !int.TryParse(argument.Trim(), out var id))
        {
            await s.WriteAsync("501 Syntax: SITE REQDEL <id>\r\n", cancellationToken);
            return;
        }

        if (!reg.Delete(id))
        {
            await s.WriteAsync($"550 Request #{id} not found.\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync($"200 Request #{id} deleted.\r\n", cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE REQWIPE
// ---------------------------------------------------------------------------
public sealed class SiteReqwipeCommand : SiteCommandBase
{
    public override string Name => "REQWIPE";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "REQWIPE  - wipe all requests (open and filled).";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var reg = context.Runtime.RequestRegistry;

        if (reg is null)
        {
            await s.WriteAsync("550 Request registry not available.\r\n", cancellationToken);
            return;
        }

        reg.WipeAll();
        await s.WriteAsync("200 All requests wiped.\r\n", cancellationToken);
    }
}
