/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteKickCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 08:50:00
 *  Last Modified:  2025-12-14 21:31:46
 *  CRC32:          0xB2AD5941
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
    public sealed class SiteKickCommand : SiteCommandBase
    {
        public override string Name => "KICK";
        public override bool RequiresAdmin => false;
        public override bool RequiresSiteop => true;
        public override string HelpText => "KICK <user>  - disconnect all sessions of user";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var s = context.Session;

            if (string.IsNullOrWhiteSpace(argument))
            {
                await s.WriteAsync("501 Syntax: SITE KICK <user>\r\n", cancellationToken);
                return;
            }

            var userName = argument.Trim();

            var bus = context.Runtime.EventBus;
            if (bus is null)
            {
                await s.WriteAsync("550 No event bus / session registry available.\r\n", cancellationToken);
                return;
            }

            var sessions = bus.GetActiveSessions();
            var targets = sessions
                .Where(sess => sess.Account?.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (targets.Count == 0)
            {
                await s.WriteAsync("200 No active sessions for that user.\r\n", cancellationToken);
                return;
            }

            foreach (var sess in targets)
            {
                try
                {
                    await sess.WriteAsync("421 Kicked by SITE KICK.\r\n", cancellationToken);

                    // Mark the session as quitting so RunAsync loop will stop
                    sess.MarkQuit();

                    // Force-close the underlying TCP connection
                    try
                    {
                        sess.Control.Close();
                    }
                    catch
                    {
                        // ignore; best-effort
                    }
                }
                catch
                {
                    // best effort
                }
            }

            await s.WriteAsync($"200 Kicked {targets.Count} session(s) for {userName}.\r\n", cancellationToken);
        }
    }
}
