/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteDbbackupCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:28:48
 *  Last Modified:  2025-12-10 03:29:05
 *  CRC32:          0x8AA7D62D
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




using amFTPd.Db;
using amFTPd.Logging;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDbbackupCommand : SiteCommandBase
{
    public override string Name => "DBBACKUP";
    public override bool RequiresAdmin => true;
    public override string HelpText => "DBBACKUP";
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
            maint.CreateBackup();
            await context.Session.WriteAsync("200 DBBACKUP completed.\r\n", cancellationToken);
        }
        catch (Exception ex)
        {
            context.Log.Log(FtpLogLevel.Error, "SITE DBBACKUP failed.", ex);
            await context.Session.WriteAsync("550 DBBACKUP failed. See log for details.\r\n", cancellationToken);
        }
    }
}