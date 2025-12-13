/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteWipeCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-13 04:45:42
 *  CRC32:          0x429C9D22
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

public sealed class SiteWipeCommand : SiteCommandBase
{
    public override string Name => "WIPE";
    public override bool RequiresAdmin => true;
    public override string HelpText => "WIPE <virt-path>  - delete file or directory";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE WIPE <virt-path>\r\n", cancellationToken);
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

        try
        {
            if (File.Exists(phys))
            {
                File.Delete(phys);
            }
            else if (Directory.Exists(phys))
            {
                Directory.Delete(phys, recursive: true);
            }
            else
            {
                await s.WriteAsync("550 Not found.\r\n", cancellationToken);
                return;
            }
        }
        catch
        {
            await s.WriteAsync("550 Failed to wipe path.\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync("200 Wipe complete.\r\n", cancellationToken);
    }
}