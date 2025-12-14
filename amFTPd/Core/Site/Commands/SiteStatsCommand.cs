/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteStatsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 10:24:34
 *  Last Modified:  2025-12-14 18:00:46
 *  CRC32:          0xE6BCEFFB
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Core.Stats;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteStatsCommand : SiteCommandBase
{
    public override string Name => "STATS";
    public override bool RequiresAdmin => false;
    public override string HelpText => "STATS - show basic server statistics";

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

        if (compat.GlStyleSiteStat)
        {
            var text =
                "200-Server statistics (gl-style)\r\n" +
                $"  Online users : {sessions.Count}\r\n" +
                $"  Transfers    : {perf.TotalTransfers} (max {perf.MaxConcurrentTransfers} concurrent)\r\n" +
                $"  Bytes up     : {perf.BytesUploaded}\r\n" +
                $"  Bytes down   : {perf.BytesDownloaded}\r\n" +
                "200 End.\r\n";

            await s.WriteAsync(text, cancellationToken);
        }
        else
        {
            var text =
                "200-Server statistics\r\n" +
                $"  Online users     : {sessions.Count}\r\n" +
                $"  Active transfers : {perf.ActiveTransfers}\r\n" +
                $"  Total transfers  : {perf.TotalTransfers}\r\n" +
                $"  Bytes uploaded   : {perf.BytesUploaded}\r\n" +
                $"  Bytes downloaded : {perf.BytesDownloaded}\r\n" +
                $"  Avg ms/transfer  : {perf.AverageTransferMilliseconds:0.0}\r\n" +
                "200 End.\r\n";

            await s.WriteAsync(text, cancellationToken);
        }
    }
}