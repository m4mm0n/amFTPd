/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteWipeCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-14 21:43:29
 *  CRC32:          0x42C5404A
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


using amFTPd.Core.Zipscript;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteWipeCommand : SiteCommandBase
{
    public override string Name => "WIPE";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "WIPE <virt-path>  - delete file or directory";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Usage: SITE WIPE <path>\r\n", cancellationToken);
            return;
        }

        var virt = FtpPath.Normalize(s.Cwd, argument);

        string? phys;
        try
        {
            phys = context.Router.FileSystem.MapToPhysical(virt);
        }
        catch
        {
            await s.WriteAsync("550 Permission denied.\r\n", cancellationToken);
            return;
        }

        var isDir = Directory.Exists(phys);
        var isFile = File.Exists(phys);

        if (!isDir && !isFile)
        {
            await s.WriteAsync("550 File or directory not found.\r\n", cancellationToken);
            return;
        }

        var section = context.Router.GetSectionForVirtual(virt);
        var user = s.Account?.UserName;
        var now = DateTimeOffset.UtcNow;

        // Zipscript: notify before we delete on disk, so we know the old phys path.
        if (context.Runtime.Zipscript is not null)
        {
            var delCtx = new ZipscriptDeleteContext(
                section.Name,
                virt,
                phys,
                IsDirectory: isDir,
                user,
                now);

            context.Runtime.Zipscript.OnDelete(delCtx);
        }

        // Perform the actual wipe
        try
        {
            if (isDir)
            {
                if (phys != null) Directory.Delete(phys, recursive: true);
            }
            else if (phys != null) File.Delete(phys);
        }
        catch
        {
            await s.WriteAsync("550 Failed to wipe.\r\n", cancellationToken);
            return;
        }

        // Dupe store: drop dupe entry for this release if there is one (best-effort)
        if (isDir && context.Runtime.DupeStore is { } dupeStore)
        {
            var trimmed = virt.TrimEnd('/', '\\');
            var releaseName = Path.GetFileName(trimmed);

            try
            {
                dupeStore.Remove(section.Name, releaseName);
            }
            catch
            {
                // ignore
            }
        }

        await s.WriteAsync("200 WIPE command successful.\r\n", cancellationToken);
    }
}