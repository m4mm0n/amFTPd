/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteSymlinkCommands.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24 00:00:00
 *  Last Modified:  2026-04-24 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      SITE commands for managing virtual-path symlinks:
 *
 *          SITE LINK   <target_vpath> <link_vpath>
 *              Create (or replace) a virtual symlink: link_vpath → target_vpath.
 *              Siteop required.
 *
 *          SITE UNLINK <link_vpath>
 *              Remove an existing virtual symlink.
 *              Siteop required.
 *
 *          SITE LINKS  [pattern]
 *              List all registered symlinks, optionally filtered by glob-style pattern.
 *              Accessible to all users.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

namespace amFTPd.Core.Site.Commands;

// ---------------------------------------------------------------------------
// SITE LINK <target_vpath> <link_vpath>
// ---------------------------------------------------------------------------

/// <summary>
/// SITE LINK &lt;target&gt; &lt;linkname&gt;
/// Creates (or replaces) a virtual symlink in the VFS.
/// </summary>
public sealed class SiteLinkCommand : SiteCommandBase
{
    public override string Name => "LINK";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "LINK <target_vpath> <link_vpath>  - create virtual symlink link→target";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var store = context.Runtime.SymlinkStore;

        if (store is null)
        {
            await s.WriteAsync("550 Symlink store not available.\r\n", cancellationToken);
            return;
        }

        // Expect two arguments: target linkname
        var parts = argument.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            await s.WriteAsync(
                "501 Syntax: SITE LINK <target_vpath> <link_vpath>\r\n",
                cancellationToken);
            return;
        }

        var target = parts[0].Trim();
        var link = parts[1].Trim();

        if (!target.StartsWith('/') || !link.StartsWith('/'))
        {
            await s.WriteAsync(
                "501 Both target and link must be absolute virtual paths (start with /).\r\n",
                cancellationToken);
            return;
        }

        // Prevent a link from pointing to itself
        if (target.Equals(link, StringComparison.OrdinalIgnoreCase))
        {
            await s.WriteAsync("550 Link path and target path cannot be the same.\r\n", cancellationToken);
            return;
        }

        var actor = s.Account?.UserName ?? "*unknown*";
        var wasNew = store.AddOrReplace(link, target, actor);
        var verb = wasNew ? "Created" : "Replaced";

        context.Runtime.AuditLog?.Log(
            actor: actor,
            action: "LINK",
            target: link,
            detail: $"target={target}",
            ip: s.RemoteEndPoint?.Address.ToString());

        await s.WriteAsync(
            $"200 {verb} symlink: {link} → {target}\r\n",
            cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE UNLINK <link_vpath>
// ---------------------------------------------------------------------------

/// <summary>
/// SITE UNLINK &lt;link_vpath&gt;
/// Removes a virtual symlink from the VFS.
/// </summary>
public sealed class SiteUnlinkCommand : SiteCommandBase
{
    public override string Name => "UNLINK";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "UNLINK <link_vpath>  - remove a virtual symlink";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var store = context.Runtime.SymlinkStore;

        if (store is null)
        {
            await s.WriteAsync("550 Symlink store not available.\r\n", cancellationToken);
            return;
        }

        var link = argument.Trim();

        if (string.IsNullOrWhiteSpace(link))
        {
            await s.WriteAsync("501 Syntax: SITE UNLINK <link_vpath>\r\n", cancellationToken);
            return;
        }

        if (!link.StartsWith('/'))
            link = "/" + link;

        var removed = store.Remove(link);

        if (!removed)
        {
            await s.WriteAsync($"550 No symlink found at: {link}\r\n", cancellationToken);
            return;
        }

        context.Runtime.AuditLog?.Log(
            actor: s.Account?.UserName ?? "*unknown*",
            action: "UNLINK",
            target: link,
            detail: null,
            ip: s.RemoteEndPoint?.Address.ToString());

        await s.WriteAsync($"200 Symlink removed: {link}\r\n", cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE LINKS [pattern]
// ---------------------------------------------------------------------------

/// <summary>
/// SITE LINKS [pattern]
/// Lists all registered virtual symlinks, optionally filtered by a substring pattern.
/// </summary>
public sealed class SiteLinksCommand : SiteCommandBase
{
    public override string Name => "LINKS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText =>
        "LINKS [pattern]  - list virtual symlinks (optional filter)";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var store = context.Runtime.SymlinkStore;

        if (store is null)
        {
            await s.WriteAsync("550 Symlink store not available.\r\n", cancellationToken);
            return;
        }

        var filter = argument.Trim();
        var links = store.GetAll();

        if (!string.IsNullOrEmpty(filter))
            links = links
                .Where(l => l.LinkPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
                         || l.TargetPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (links.Count == 0)
        {
            var noMatch = string.IsNullOrEmpty(filter)
                ? "200 No virtual symlinks registered."
                : $"200 No symlinks matching \"{filter}\".";
            await s.WriteAsync(noMatch + "\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync($"200-Virtual symlinks ({links.Count}):\r\n", cancellationToken);

        // Determine column width
        var maxLinkLen = links.Max(l => l.LinkPath.Length);

        foreach (var lnk in links)
        {
            var padding = new string(' ', maxLinkLen - lnk.LinkPath.Length + 1);
            var ts = lnk.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm");
            await s.WriteAsync(
                $" {lnk.LinkPath}{padding}→  {lnk.TargetPath}  (by {lnk.CreatedBy}, {ts})\r\n",
                cancellationToken);
        }

        await s.WriteAsync($"200 End of symlink list.\r\n", cancellationToken);
    }
}
