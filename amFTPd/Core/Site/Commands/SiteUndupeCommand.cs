/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteUndupeCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 12:49:39
 *  Last Modified:  2025-12-14 21:42:24
 *  CRC32:          0xF446E280
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
    public sealed class SiteUndupeCommand : SiteCommandBase
    {
        public override string Name => "UNDUPE";
        public override bool RequiresAdmin => false;
        public override bool RequiresSiteop => true;
        public override string HelpText =>
            "UNDUPE <release> <section> - remove a dupe entry.";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var s = context.Session;

            if (context.Runtime.DupeStore is null)
            {
                await s.WriteAsync("550 Dupe store is not enabled.\r\n", cancellationToken);
                return;
            }

            var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await s.WriteAsync("501 Usage: SITE UNDUPE <release> <section>\r\n", cancellationToken);
                return;
            }

            var releaseName = parts[0];
            var sectionName = parts[1];

            var dupeStore = context.Runtime.DupeStore;

            bool removed;
            try
            {
                // API assumption: Remove(sectionName, releaseName) -> bool
                removed = dupeStore.Remove(sectionName, releaseName);
            }
            catch
            {
                await s.WriteAsync("550 Failed to update dupe store.\r\n", cancellationToken);
                return;
            }

            if (!removed)
            {
                await s.WriteAsync("211 No such dupe entry.\r\n", cancellationToken);
                return;
            }

            await s.WriteAsync(
                $"250 UNDUPE removed {releaseName} from section {sectionName}\r\n",
                cancellationToken);
        }
    }
}
