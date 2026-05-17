/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SitePreApproveCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24 00:00:00
 *  Last Modified:  2026-04-24 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      SITE PREAPPROVE <releasename>
 *      Staff command: approves a pending pre, making it live.
 *
 *      SITE PREDENY <releasename> [reason]
 *      Staff command: denies a pending pre and removes it from the queue.
 *
 *      SITE PREPENDIG
 *      Lists all pre entries currently awaiting approval.
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
using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Pre;

namespace amFTPd.Core.Site.Commands;

// ---------------------------------------------------------------------------
// SITE PREAPPROVE <releasename>
// ---------------------------------------------------------------------------

/// <summary>
/// Approves a pending pre, promoting it from the queue to the live registry.
/// </summary>
public sealed class SitePreApproveCommand : SiteCommandBase
{
    public override string Name => "PREAPPROVE";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "PREAPPROVE <release>  - approve a pending pre and make it live";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var runtime = context.Runtime;

        var releaseName = argument.Trim();
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            await s.WriteAsync("501 Syntax: SITE PREAPPROVE <releasename>\r\n", cancellationToken);
            return;
        }

        if (!runtime.PreRegistry.TryGetPending(releaseName, out var pending) || pending is null)
        {
            await s.WriteAsync($"550 No pending pre found for: {releaseName}\r\n", cancellationToken);
            return;
        }

        var actor = s.Account?.UserName ?? "unknown";
        var now = DateTimeOffset.UtcNow;
        var approved = pending with
        {
            Status = PreStatus.Approved,
            ReviewedBy = actor,
            ReviewedAtUtc = now
        };

        // Promote: remove from queue, add to live registry
        runtime.PreRegistry.RemovePending(releaseName);
        runtime.PreRegistry.AddOrReplace(approved);
        context.SceneRegistry.MarkPre(approved.Section, approved.VirtualPath);

        // Update dupe DB
        var dupeStore = runtime.DupeStore;
        if (dupeStore is not null)
        {
            var existing = dupeStore.Find(approved.Section, approved.ReleaseName);
            var dupeEntry = (existing ?? new DupeEntry()) with
            {
                ReleaseName = approved.ReleaseName,
                SectionName = approved.Section,
                VirtualPath = approved.VirtualPath,
                TotalBytes = approved.TotalBytes,
                FirstSeen = approved.Timestamp,
                LastUpdated = now,
                UploaderUser = approved.User,
                UploaderGroup = approved.Group
            };
            dupeStore.Upsert(dupeEntry);
        }

        // Announce via EventBus
        runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.Pre,
            Timestamp = now,
            SessionId = s.SessionId,
            User = approved.User,
            Group = approved.Group,
            Section = approved.Section,
            VirtualPath = approved.VirtualPath,
            ReleaseName = approved.ReleaseName,
            Extra = "approved"
        });

        runtime.AuditLog?.Log(
            actor: actor,
            action: "PREAPPROVE",
            target: releaseName,
            detail: $"section={approved.Section} tagger={approved.User} group={approved.Group}",
            ip: s.RemoteEndPoint?.Address.ToString());

        await s.WriteAsync(
            $"200 PRE approved: {approved.Section} {approved.ReleaseName}\r\n",
            cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE PREDENY <releasename> [reason]
// ---------------------------------------------------------------------------

/// <summary>
/// Denies a pending pre, removing it from the queue with an optional reason.
/// </summary>
public sealed class SitePreDenyCommand : SiteCommandBase
{
    public override string Name => "PREDENY";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "PREDENY <release> [reason]  - deny a pending pre";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        var parts = argument.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            await s.WriteAsync("501 Syntax: SITE PREDENY <releasename> [reason]\r\n", cancellationToken);
            return;
        }

        var releaseName = parts[0];
        var reason = parts.Length > 1 ? parts[1] : null;
        var runtime = context.Runtime;

        if (!runtime.PreRegistry.TryGetPending(releaseName, out var pending) || pending is null)
        {
            await s.WriteAsync($"550 No pending pre found for: {releaseName}\r\n", cancellationToken);
            return;
        }

        var actor = s.Account?.UserName ?? "unknown";
        runtime.PreRegistry.RemovePending(releaseName);

        runtime.AuditLog?.Log(
            actor: actor,
            action: "PREDENY",
            target: releaseName,
            detail: $"section={pending.Section} tagger={pending.User} reason={reason ?? "(none)"}",
            ip: s.RemoteEndPoint?.Address.ToString());

        var reasonSuffix = reason is not null ? $" Reason: {reason}" : "";
        await s.WriteAsync(
            $"200 PRE denied: {releaseName}.{reasonSuffix}\r\n",
            cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE PREPENDING
// ---------------------------------------------------------------------------

/// <summary>
/// Lists all pre entries that are currently awaiting staff approval.
/// </summary>
public sealed class SitePrePendingCommand : SiteCommandBase
{
    public override string Name => "PREPENDING";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "PREPENDING  - list pres awaiting approval";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var pending = context.Runtime.PreRegistry.PendingAll;

        if (pending.Count == 0)
        {
            await s.WriteAsync("200 No pres pending approval.\r\n", cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"200-Pending pre approvals ({pending.Count}):");

        foreach (var p in pending)
        {
            var group = p.Group is not null ? $" ({p.Group})" : "";
            var ts = p.Timestamp.ToString("MM-dd HH:mm");
            sb.AppendLine($" {ts}  {p.Section,-10} {p.ReleaseName,-50} {p.User}{group}");
        }

        sb.AppendLine("200 End.");
        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}
