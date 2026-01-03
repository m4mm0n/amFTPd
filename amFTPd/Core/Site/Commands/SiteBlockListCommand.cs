using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteBlockListCommand : SiteCommandBase
{
    public override string Name => "BLOCKLIST";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("200-Blocked IP buckets");

        foreach (var ip in context.Runtime.LiveStats.Ips.Values
                     .Where(i => i.IsCurrentlyBlocked))
        {
            sb.AppendLine(
                $" {ip.Ip} until={ip.BlockedUntilUtc} reason={ip.BlockReason}");
        }

        sb.AppendLine("200 End.");

        await context.Session.WriteAsync(sb.ToString(), cancellationToken);
    }
}