/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteGroupdelCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 08:48:23
 *  Last Modified:  2025-12-14 21:30:44
 *  CRC32:          0xA5A5E956
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


namespace amFTPd.Core.Site.Commands;

public sealed class SiteGroupdelCommand : SiteCommandBase
{
    public override string Name => "GROUPDEL";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "GROUPDEL <group> - delete a group (if backend supports it)";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        await context.Session.WriteAsync("502 GROUPDEL not implemented for this backend.\r\n", cancellationToken);
    }
}