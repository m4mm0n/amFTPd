/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteSectionsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-14 21:37:22
 *  CRC32:          0x90E23849
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


using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteSectionsCommand : SiteCommandBase
{
    public override string Name => "SECTIONS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "SECTIONS - list configured sections";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE SECTIONS requires admin/siteop privileges.\r\n",
                cancellationToken);
            return;
        }

        var secs = context.Sections.GetSections()
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("211-Configured sections:");

        foreach (var s in secs)
        {
            sb.Append(" NAME=").Append(s.Name);
            sb.Append(" ROOT=").Append(s.VirtualRoot);
            sb.Append(" FREE=").Append(s.FreeLeech ? "Y" : "N");
            sb.Append(" RATIO=").Append(s.RatioUploadUnit).Append(':').Append(s.RatioDownloadUnit);
            sb.AppendLine();
        }

        sb.AppendLine("211 End");

        var text = sb.ToString().Replace("\n", "\r\n");
        await context.Session.WriteAsync(text, cancellationToken);
    }
}