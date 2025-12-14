/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteUptimeCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 21:14:26
 *  Last Modified:  2025-12-14 21:18:40
 *  CRC32:          0x26E91FED
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
namespace amFTPd.Core.Site.Commands
{
    public class SiteUptimeCommand : SiteCommandBase
    {
        public override string Name => "UPTIME";
        public override bool RequiresAdmin => false;
        public override string HelpText => "UPTIME";
        public override async Task ExecuteAsync(SiteCommandContext context, string argument, CancellationToken ct)
        {
            // If for some reason we don't have server info, fail gracefully
            if (context.Session.Server is null)
            {
                await context.Session.WriteAsync("451 UPTIME unavailable.\r\n", ct);
                return;
            }

            var started = context.Session.Server.StartedAt;
            var now = DateTimeOffset.UtcNow;
            var uptime = now - started;

            var days = uptime.Days;
            var hours = uptime.Hours;
            var minutes = uptime.Minutes;
            var seconds = uptime.Seconds;

            await context.Session.WriteAsync("200-Server uptime:\r\n", ct);
            await context.Session.WriteAsync(
                $"200-  Started : {started:yyyy-MM-dd HH:mm:ss zzz}\r\n", ct);
            await context.Session.WriteAsync(
                $"200-  Uptime  : {days} days {hours} hours {minutes} minutes {seconds} seconds\r\n", ct);
            await context.Session.WriteAsync("200 End of UPTIME.\r\n", ct);
        }
    }
}
