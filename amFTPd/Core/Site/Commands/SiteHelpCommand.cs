/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteHelpCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 00:06:48
 *  Last Modified:  2025-12-14 21:31:25
 *  CRC32:          0x9484CFDF
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
    public sealed class SiteHelpCommand : SiteCommandBase
    {
        public override string Name => "HELP";
        public override string HelpText => "HELP [cmd] - show available SITE commands or help for one command";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var all = SiteCommandRegistry.Build(context);

            var arg = argument?.Trim();
            if (!string.IsNullOrWhiteSpace(arg))
            {
                // Specific command help
                if (all.TryGetValue(arg.ToUpperInvariant(), out var cmd))
                {
                    var line =
                        $"214-SITE {cmd.Name}: {cmd.HelpText}\r\n" +
                        "214 End.\r\n";
                    await context.Session.WriteAsync(line, cancellationToken);
                }
                else
                {
                    await context.Session.WriteAsync(
                        "550 Unknown SITE command.\r\n",
                        cancellationToken);
                }

                return;
            }

            // General help
            var sb = new StringBuilder();
            sb.AppendLine("214-Available SITE commands:");

            foreach (var kvp in all
                         .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var cmd = kvp.Value;
                sb.Append("  ");
                sb.Append(cmd.Name.PadRight(12));
                sb.Append(" - ");
                sb.AppendLine(cmd.HelpText);
            }

            sb.AppendLine("214 End.");

            var text = sb.ToString().Replace("\n", "\r\n");
            await context.Session.WriteAsync(text, cancellationToken);
        }
    }
}
