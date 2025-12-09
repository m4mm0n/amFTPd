/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteGroupaddCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 08:47:39
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x2F1A54CC
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
    public sealed class SiteGroupaddCommand : SiteCommandBase
    {
        public override string Name => "GROUPADD";
        public override bool RequiresAdmin => true;
        public override string HelpText => "GROUPADD <group> - create a group (if backend supports it)";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            await context.Session.WriteAsync("502 GROUPADD not implemented for this backend.\r\n", cancellationToken);
        }
    }
}
