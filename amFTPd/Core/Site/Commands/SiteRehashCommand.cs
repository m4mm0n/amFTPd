/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRehashCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 15:12:30
 *  Last Modified:  2025-12-14 15:12:42
 *  CRC32:          0x2897DA57
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
using System.Text;

namespace amFTPd.Core.Site.Commands
{
    public class SiteRehashCommand : SiteCommandBase
    {
        public override string Name => "REHASH";
        public override bool RequiresAdmin => true;
        public override string HelpText => "REHASH - reload configuration from disk";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var s = context.Session;
            var acc = s.Account;

            if (acc is not { IsAdmin: true })
            {
                await s.WriteAsync("550 SITE REHASH requires admin privileges.\r\n", cancellationToken);
                return;
            }

            var (success, message, changedSections) =
                await context.Server.ReloadConfigurationAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (!success)
            {
                // message already includes a human-readable reason
                await s.WriteAsync($"550 {message}\r\n", cancellationToken);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("200-Configuration reloaded.");

            if (changedSections.Count == 0)
            {
                sb.AppendLine("200 No changes detected (config file is identical).");
            }
            else
            {
                sb.AppendLine("200-Changed sections:");
                foreach (var section in changedSections)
                {
                    sb.Append("200  - ").AppendLine(section);
                }
            }

            sb.AppendLine("200 End.");

            var text = sb.ToString().Replace("\n", "\r\n");
            await s.WriteAsync(text, cancellationToken);
        }
    }
}
