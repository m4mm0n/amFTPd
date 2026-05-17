/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteAffilCommands.cs
 *  Created:        2026-04-24
 *  Description:    SITE AFFIL / DEAFFIL / AFFILS
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text;

namespace amFTPd.Core.Site.Commands;

// ---------------------------------------------------------------------------
// SITE AFFIL <section> <group>
// ---------------------------------------------------------------------------
public sealed class SiteAffilCommand : SiteCommandBase
{
    public override string Name => "AFFIL";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "AFFIL <section> <group>  - affiliate a group with a section.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var store = context.Runtime.AffilStore;

        if (store is null)
        {
            await s.WriteAsync("550 Affil store not available.\r\n", cancellationToken);
            return;
        }

        var parts = argument?.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 })
        {
            await s.WriteAsync("501 Syntax: SITE AFFIL <section> <group>\r\n", cancellationToken);
            return;
        }

        var section = parts[0];
        var group = parts[1];

        if (!store.Add(section, group))
        {
            await s.WriteAsync(
                $"200 {group} is already affiliated with {section}.\r\n",
                cancellationToken);
            return;
        }

        await s.WriteAsync(
            $"200 {group} affiliated with section {section}.\r\n",
            cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE DEAFFIL <section> <group>
// ---------------------------------------------------------------------------
public sealed class SiteDeaffilCommand : SiteCommandBase
{
    public override string Name => "DEAFFIL";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "DEAFFIL <section> <group>  - remove a group's affiliation with a section.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var store = context.Runtime.AffilStore;

        if (store is null)
        {
            await s.WriteAsync("550 Affil store not available.\r\n", cancellationToken);
            return;
        }

        var parts = argument?.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 })
        {
            await s.WriteAsync("501 Syntax: SITE DEAFFIL <section> <group>\r\n", cancellationToken);
            return;
        }

        var section = parts[0];
        var group = parts[1];

        if (!store.Remove(section, group))
        {
            await s.WriteAsync(
                $"550 {group} is not affiliated with {section}.\r\n",
                cancellationToken);
            return;
        }

        await s.WriteAsync(
            $"200 {group} removed from affils of section {section}.\r\n",
            cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// SITE AFFILS [section]
// ---------------------------------------------------------------------------
public sealed class SiteAffilsCommand : SiteCommandBase
{
    public override string Name => "AFFILS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "AFFILS [section]  - list affiliated groups, optionally filtered by section.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var store = context.Runtime.AffilStore;

        if (store is null)
        {
            await s.WriteAsync("550 Affil store not available.\r\n", cancellationToken);
            return;
        }

        var sectionFilter = string.IsNullOrWhiteSpace(argument) ? null : argument.Trim();
        var all = store.GetAll(sectionFilter);

        if (all.Count == 0)
        {
            var msg = sectionFilter is not null
                ? $"200 No affils for section '{sectionFilter}'.\r\n"
                : "200 No affils configured.\r\n";
            await s.WriteAsync(msg, cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("200-Affiliations:");
        foreach (var (sec, groups) in all.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  {sec,-20}: {string.Join(", ", groups)}");
        sb.Append("200 End.\r\n");

        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}
