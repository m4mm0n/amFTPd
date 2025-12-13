/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SitePurgeCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 08:52:30
 *  Last Modified:  2025-12-13 04:45:42
 *  CRC32:          0x2BD15E4F
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
 * ==================================================================================================== */









namespace amFTPd.Core.Site.Commands
{
    public sealed class SitePurgeCommand : SiteCommandBase
    {
        public override string Name => "PURGE";
        public override bool RequiresAdmin => true;
        public override string HelpText => "PURGE <virt-path>  - recursively delete directory tree";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var s = context.Session;

            if (string.IsNullOrWhiteSpace(argument))
            {
                await s.WriteAsync("501 Syntax: SITE PURGE <virt-path>\r\n", cancellationToken);
                return;
            }

            var virt = FtpPath.Normalize(s.Cwd, argument);

            string? phys;
            try
            {
                phys = context.Router.FileSystem.MapToPhysical(virt);
            }
            catch
            {
                await s.WriteAsync("550 Permission denied.\r\n", cancellationToken);
                return;
            }

            if (!Directory.Exists(phys))
            {
                await s.WriteAsync("550 Directory not found.\r\n", cancellationToken);
                return;
            }

            try
            {
                Directory.Delete(phys, recursive: true);
            }
            catch
            {
                await s.WriteAsync("550 Failed to purge directory.\r\n", cancellationToken);
                return;
            }

            await s.WriteAsync("200 Directory purged.\r\n", cancellationToken);
        }
    }
}
