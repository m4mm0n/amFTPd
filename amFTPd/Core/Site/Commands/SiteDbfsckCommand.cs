/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteDbfsckCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:21:49
 *  Last Modified:  2025-12-14 21:28:31
 *  CRC32:          0xD22DF666
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


using amFTPd.Db;
using amFTPd.Logging;

namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteDbfsckCommand : SiteCommandBase
    {
        public override string Name => "DBFSCK";
        public override bool RequiresAdmin => true;
        public override string HelpText => "DBFSCK";
        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            if (context.Router.Runtime.Database is null)
            {
                await context.Session.WriteAsync("550 Database backend not enabled.\r\n", cancellationToken);
                return;
            }

            try
            {
                var maint = new DatabaseMaintenance(context.Router.Runtime.Database, context.Log);
                maint.RunFsck();
                await context.Session.WriteAsync("200 DBFSCK completed.\r\n", cancellationToken);
            }
            catch (Exception ex)
            {
                context.Log.Log(FtpLogLevel.Error, "SITE DBFSCK failed.", ex);
                await context.Session.WriteAsync("550 DBFSCK failed. See log for details.\r\n", cancellationToken);
            }
        }
    }
}
