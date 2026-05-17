/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteAddlineCommand.cs
 *  Created:        2026-04-24
 *  Description:    SITE ADDLINE <message> — post a oneliner to the shoutbox.
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using amFTPd.Core.Events;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteAddlineCommand : SiteCommandBase
{
    public override string Name => "ADDLINE";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "ADDLINE <message>  - post a message to the shoutbox.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE ADDLINE <message>\r\n", cancellationToken);
            return;
        }

        var store = context.Runtime.OnelineStore;
        if (store is null)
        {
            await s.WriteAsync("550 Oneliner store is not enabled.\r\n", cancellationToken);
            return;
        }

        var user = s.Account?.UserName ?? "unknown";
        var group = s.Account?.GroupName;
        var msg = argument.Trim();

        if (msg.Length > 200)
            msg = msg[..200];

        var entry = store.Add(user, group, msg);

        context.Runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.Oneliner,
            Timestamp = entry.PostedAt,
            SessionId = s.SessionId,
            User = user,
            Group = group,
            Reason = msg,
            Extra = $"id={entry.Id}"
        });

        await s.WriteAsync($"200 Oneliner #{entry.Id} posted.\r\n", cancellationToken);
    }
}
