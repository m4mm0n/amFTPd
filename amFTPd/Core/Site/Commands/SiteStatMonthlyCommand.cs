/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteStatMonthlyCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 16:57:53
 *  Last Modified:  2025-12-14 21:40:44
 *  CRC32:          0xAAB9F660
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

public sealed class SiteStatMonthlyCommand : SiteCommandBase
{
    public override string Name => "STATMONTHLY";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "STATMONTHLY [JSON] - show upload/download stats for the last 30 days";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var session = context.Session;
        var runtime = context.Runtime;

        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-30);

        var logPath = SessionLogStatsService.GetDefaultLogPath(runtime);
        var stats = SessionLogStatsService.Compute(logPath, from, now);

        var arg = argument?.Trim();
        var wantsJson = !string.IsNullOrEmpty(arg) &&
                        arg.Equals("JSON", StringComparison.OrdinalIgnoreCase);

        if (wantsJson)
        {
            var json = SessionLogStatsService.ToJsonPayload("monthly", stats);
            var text =
                $"200-STATMONTHLY {json}\r\n" +
                "200 End.\r\n";

            await session.WriteAsync(text, cancellationToken);
            return;
        }

        var response =
            "200-Monthly statistics (last 30 days)\r\n" +
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