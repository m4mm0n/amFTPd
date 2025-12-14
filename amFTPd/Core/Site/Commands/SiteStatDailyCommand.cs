/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteStatDailyCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 16:56:42
 *  Last Modified:  2025-12-14 21:40:35
 *  CRC32:          0x4454473F
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

namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteStatDailyCommand : SiteCommandBase
    {
        public override string Name => "STATDAILY";
        public override bool RequiresAdmin => false;
        public override bool RequiresSiteop => true;
        public override string HelpText => "STATDAILY [JSON] - show upload/download stats for the last 24 hours";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var session = context.Session;
            var runtime = context.Runtime;

            var now = DateTimeOffset.UtcNow;
            var from = now.AddDays(-1);

            var logPath = SessionLogStatsService.GetDefaultLogPath(runtime);
            var stats = SessionLogStatsService.Compute(logPath, from, now);

            var arg = argument?.Trim();
            var wantsJson = !string.IsNullOrEmpty(arg) &&
                            arg.Equals("JSON", StringComparison.OrdinalIgnoreCase);

            if (wantsJson)
            {
                var json = SessionLogStatsService.ToJsonPayload("daily", stats);
                var text =
                    $"200-STATDAILY {json}\r\n" +
                    "200 End.\r\n";

                await session.WriteAsync(text, cancellationToken);
                return;
            }

            var response =
                "200-Daily statistics (last 24h)\r\n" +
                $"  Period    : {from:u} - {now:u}\r\n" +
                $"  Uploads   : {stats.Uploads}\r\n" +
                $"  Downloads : {stats.Downloads}\r\n" +
                $"  Nukes     : {stats.Nukes}\r\n" +
                $"  Pres      : {stats.Pres}\r\n" +
                $"  Bytes up  : {stats.BytesUploaded}\r\n" +
                $"  Bytes down: {stats.BytesDownloaded}\r\n" +
                $"  Logins    : {stats.Logins}\r\n" +
                $"  Users     : {stats.UniqueUsers}\r\n" +
                "200 End.\r\n";

            await session.WriteAsync(response, cancellationToken);
        }
    }
}
