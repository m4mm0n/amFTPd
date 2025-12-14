/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteVersCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 10:24:35
 *  Last Modified:  2025-12-14 17:59:50
 *  CRC32:          0x94EDB9C2
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


using System.Reflection;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteVersCommand : SiteCommandBase
{
    public override string Name => "VERS";
    public override bool RequiresAdmin => false;
    public override string HelpText => "VERS - show amFTPd version and compatibility profile";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var asm = Assembly.GetExecutingAssembly().GetName();
        var ver = asm.Version?.ToString() ?? "unknown";

        var compat = context.Runtime.FtpConfig.Compatibility;
        var compatTag = SiteCommandRouterCompat.DescribeCompat(compat);

        var line = $"200 amFTPd {ver} ({compatTag})\r\n";
        await s.WriteAsync(line, cancellationToken);
    }
}