using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteWhoIpCommand : SiteCommandBase
{
    public override string Name => "WHOIP";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "WHOIP [FULL|USERS|ip_xxxx]";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var status = context.StatusEndpoint;
        var live = context.Runtime.LiveStats;

        if (status is null)
        {
            await context.Session.WriteAsync(
                "550 Status endpoint is disabled.\r\n",
                cancellationToken);
            return;
        }

        var payload = status.BuildStatusPayload(includeIpStats: true);
        var ips = payload.Ips;

        if (ips is null || ips.Count == 0)
        {
            await context.Session.WriteAsync(
                "200-No active IPs.\r\n",
                cancellationToken);
            return;
        }

        var mode = argument?.Trim() ?? string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("200-Active IPs (anonymized)");

        foreach (var kv in ips)
        {
            var ipKey = kv.Key;
            var ip = kv.Value;

            var users = live.Users.Values
                .Where(u => u.CurrentIpKey == ipKey)
                .Select(u => u.UserName)
                .ToList();

            sb.AppendLine(
                $" {ipKey,-10} " +
                $"sessions={ip.ActiveSessions} " +
                $"users={users.Count} " +
                $"up={ip.BytesUploaded} " +
                $"dn={ip.BytesDownloaded}");

            if (mode.Equals("USERS", StringComparison.OrdinalIgnoreCase) ||
                mode.Equals("FULL", StringComparison.OrdinalIgnoreCase) ||
                mode.Equals(ipKey, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var u in users)
                    sb.AppendLine($"    - {u}");
            }
        }

        sb.AppendLine("200 End.");

        await context.Session.WriteAsync(sb.ToString(), cancellationToken);
    }
}