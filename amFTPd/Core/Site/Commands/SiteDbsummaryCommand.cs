/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteDbsummaryCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:28:48
 *  Last Modified:  2025-12-10 03:30:22
 *  CRC32:          0x1D9DB44D
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




using amFTPd.Logging;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDbsummaryCommand : SiteCommandBase
{
    public override string Name => "DBSUMMARY";
    public override bool RequiresAdmin => true;
    public override string HelpText => "DBSUMMARY";
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
            context.Router.Runtime.Database.PrintSummary();
            await context.Session.WriteAsync("200 DBSUMMARY completed.\r\n", cancellationToken);
        }
        catch (Exception ex)
        {
            context.Log.Log(FtpLogLevel.Error, "SITE DBSUMMARY failed.", ex);
            await context.Session.WriteAsync("550 DBSUMMARY failed. See log for details.\r\n", cancellationToken);
        }
    }
}