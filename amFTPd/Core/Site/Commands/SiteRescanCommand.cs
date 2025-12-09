/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRescanCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 09:00:57
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xFEEB1272
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

public sealed class SiteRescanCommand : SiteCommandBase
{
    public override string Name => "RESCAN";
    public override bool RequiresAdmin => true;
    public override string HelpText => "RESCAN <path> - rescan a release (zipscript integration)";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        await context.Session.WriteAsync("502 RESCAN not implemented yet.\r\n", cancellationToken);
    }
}