/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteGroupsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-14 21:31:18
 *  CRC32:          0x7D420CD3
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

public sealed class SiteGroupsCommand : SiteCommandBase
{
    public override string Name => "GROUPS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "GROUPS - list all configured groups";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE GROUPS requires admin privileges.\r\n",
                cancellationToken);
            return;
        }

        var groups = context.Runtime.Groups
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("211-Configured groups:");

        foreach (var (name, cfg) in groups)
        {
            sb.Append(" NAME=").Append(name);
            sb.Append(" DESC=").Append(cfg.Description);
            sb.Append(" RATIO_MUL=").Append(cfg.RatioMultiply);
            sb.Append(" UL_BONUS=").Append(cfg.UploadBonus);
            sb.Append(" FLAGS=").Append(cfg.Flags is { Count: > 0 } ? new string(cfg.Flags.ToArray()) : "-");
            sb.AppendLine();
        }

        sb.AppendLine("211 End");

        var text = sb.ToString().Replace("\n", "\r\n");
        await context.Session.WriteAsync(text, cancellationToken);
    }
}