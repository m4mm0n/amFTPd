/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteDellineCommand.cs
 *  Created:        2026-04-24
 *  Description:    SITE DELLINE <id> — delete a oneliner (staff only).
 *                  SITE WIPELINES    — wipe all oneliners (staff only).
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDellineCommand : SiteCommandBase
{
    public override string Name => "DELLINE";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "DELLINE <id>  - delete a oneliner by ID.";

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

        if (string.IsNullOrWhiteSpace(argument) ||
            !int.TryParse(argument.Trim(), out var id))
        {
            await s.WriteAsync("501 Syntax: SITE DELLINE <id>\r\n", cancellationToken);
            return;
        }

        if (!store.Delete(id))
        {
            await s.WriteAsync($"550 Oneliner #{id} not found.\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync($"200 Oneliner #{id} deleted.\r\n", cancellationToken);
    }
}

public sealed class SiteWipelinesCommand : SiteCommandBase
{
    public override string Name => "WIPELINES";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "WIPELINES  - wipe all oneliners.";

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

        var count = store.Count;
        store.WipeAll();

        await s.WriteAsync($"200 Wiped {count} oneliner(s).\r\n", cancellationToken);
    }
}
