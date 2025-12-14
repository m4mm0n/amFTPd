/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteUndupeDirCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 12:52:30
 *  Last Modified:  2025-12-14 21:42:33
 *  CRC32:          0x9524F85F
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

public sealed class SiteUndupeDirCommand : SiteCommandBase
{
    public override string Name => "UNDUPEDIR";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "UNDUPEDIR <path> - remove dupe entry based on a directory path.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (context.Runtime.DupeStore is null)
        {
            await s.WriteAsync("550 Dupe store is not enabled.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Usage: SITE UNDUPEDIR <path>\r\n", cancellationToken);
            return;
        }

        var virt = FtpPath.Normalize(s.Cwd, argument);

        Config.Ftpd.FtpSection? section;
        try
        {
            section = context.Router.GetSectionForVirtual(virt);
        }
        catch
        {
            await s.WriteAsync("550 Permission denied.\r\n", cancellationToken);
            return;
        }

        var trimmed = virt.TrimEnd('/', '\\');
        var releaseName = Path.GetFileName(trimmed);

        var dupeStore = context.Runtime.DupeStore;

        bool removed;
        try
        {
            // same API assumption as above
            removed = dupeStore.Remove(section.Name, releaseName);
        }
        catch
        {
            await s.WriteAsync("550 Failed to update dupe store.\r\n", cancellationToken);
            return;
        }

        if (!removed)
        {
            await s.WriteAsync("211 No such dupe entry.\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync(
            $"250 UNDUPEDIR removed {releaseName} from section {section.Name}\r\n",
            cancellationToken);
    }
}