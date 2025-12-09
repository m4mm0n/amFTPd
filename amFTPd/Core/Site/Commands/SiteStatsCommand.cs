/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteStatsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 10:24:34
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x467E3400
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







namespace amFTPd.Core.Site.Commands;

public sealed class SiteStatsCommand : SiteCommandBase
{
    public override string Name => "STATS";
    public override bool RequiresAdmin => true;
    public override string HelpText => "STATS - show basic server statistics";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var cfg = context.Runtime.FtpConfig;

        var userCount = context.Users.GetAllUsers().Count();
        var sectionCount = context.Sections.GetSections().Count;

        var activeSessions = context.Runtime.EventBus?.GetActiveSessions().Count ?? 0;

        var text =
            $"200-Server statistics\r\n" +
            $"  Bind: {cfg.BindAddress ?? "0.0.0.0"}:{cfg.Port}\r\n" +
            $"  Users     : {userCount}\r\n" +
            $"  Sections  : {sectionCount}\r\n" +
            $"  Sessions  : {activeSessions}\r\n" +
            $"200 End.\r\n";

        await s.WriteAsync(text, cancellationToken);
    }
}