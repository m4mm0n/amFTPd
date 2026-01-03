using amFTPd.Core.Stats;
using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteStatsCommand : SiteCommandBase
{
    public override string Name => "STATS";
    public override bool RequiresAdmin => false;
    public override string HelpText => "STATS [FULL|TOP <n>] - show server statistics";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var runtime = context.Runtime;
        var compat = runtime.FtpConfig.Compatibility;

        var perf = PerfCounters.GetSnapshot();
        var sessions = runtime.EventBus.GetActiveSessions();

        StatsSnapshot? rate = null;
        try { rate = runtime.StatsCollector?.GetSnapshot(); }
        catch { }

        var isFull =
            !string.IsNullOrWhiteSpace(argument) &&
            (argument.Equals("FULL", StringComparison.OrdinalIgnoreCase) ||
             argument.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
             argument.StartsWith("TOP", StringComparison.OrdinalIgnoreCase));

        int? topN = null;
        if (!string.IsNullOrWhiteSpace(argument))
        {
            var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                parts[0].Equals("TOP", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[1], out var n))
            {
                topN = n;
            }
        }

        var sb = new StringBuilder();

        sb.AppendLine(
            compat.GlStyleSiteStat
                ? "200-Server statistics (gl-style)"
                : "200-Server statistics");

        sb.AppendLine($"  Online users     : {sessions.Count}");
        sb.AppendLine($"  Active transfers : {perf.ActiveTransfers}");
        sb.AppendLine($"  Total transfers  : {perf.TotalTransfers}");
        sb.AppendLine($"  Bytes uploaded   : {perf.BytesUploaded}");
        sb.AppendLine($"  Bytes downloaded : {perf.BytesDownloaded}");
        sb.AppendLine($"  Avg ms/transfer  : {perf.AverageTransferMilliseconds:0.0}");

        if (rate is not null && (!compat.GlStyleSiteStat || isFull))
        {
            sb.AppendLine($"  Commands/sec     : {rate.CommandsPerSecond:0.00}");
            sb.AppendLine($"  Avg xfer ms(win) : {rate.AverageTransferDurationMs:0.0}");
        }

        if (isFull && runtime.LiveStats is not null)
        {
            sb.AppendLine();
            sb.AppendLine("  -- Rolling transfer rates --");
            sb.AppendLine($"   5s  : {runtime.RollingStats.Transfers5s.RatePerSecond():0.00} xfers/sec");
            sb.AppendLine($"   1m  : {runtime.RollingStats.Transfers1m.RatePerSecond():0.00} xfers/sec");
            sb.AppendLine($"   5m  : {runtime.RollingStats.Transfers5m.RatePerSecond():0.00} xfers/sec");

            if (runtime.LiveStats.Sections.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  -- Per-section live stats --");

                foreach (var sec in runtime.LiveStats.Sections.Values
                             .OrderByDescending(s2 => s2.BytesUploaded + s2.BytesDownloaded))
                {
                    sb.AppendLine(
                        $"   [{sec.SectionName}] " +
                        $"UL={sec.Uploads} " +
                        $"DL={sec.Downloads} " +
                        $"UP={sec.BytesUploaded} " +
                        $"DN={sec.BytesDownloaded} " +
                        $"users={sec.ActiveUsers}");
                }
            }

            if (runtime.LiveStats.Users.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  -- Per-user live stats --");

                foreach (var usr in runtime.LiveStats.Users.Values
                             .OrderByDescending(u => u.BytesUploaded + u.BytesDownloaded))
                {
                    sb.AppendLine(
                        $"   {usr.UserName,-12} " +
                        $"UL={usr.Uploads} " +
                        $"DL={usr.Downloads} " +
                        $"UP={usr.BytesUploaded} " +
                        $"DN={usr.BytesDownloaded} " +
                        $"sessions={usr.ActiveSessions}");
                }
            }

            if (context.StatusEndpoint is not null &&
                runtime.LiveStats.Ips.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(
                    topN.HasValue
                        ? $"  -- Per-IP live stats (top {topN.Value}, anonymized) --"
                        : "  -- Per-IP live stats (anonymized) --");

                var payload = context.StatusEndpoint
                    .BuildStatusPayload(includeIpStats: true, overrideMaxIps: topN);

                foreach (var kv in payload.Ips)
                {
                    var ip = kv.Value;

                    sb.AppendLine(
                        $"   {kv.Key} " +
                        $"UL={ip.Uploads} " +
                        $"DL={ip.Downloads} " +
                        $"UP={ip.BytesUploaded} " +
                        $"DN={ip.BytesDownloaded} " +
                        $"sessions={ip.ActiveSessions}");
                }
            }
        }

        sb.AppendLine("200 End.");
        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}