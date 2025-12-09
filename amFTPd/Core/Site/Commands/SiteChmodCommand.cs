/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteChmodCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x4CF90431
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

public sealed class SiteChmodCommand : SiteCommandBase
{
    public override string Name => "CHMOD";
    public override bool RequiresAdmin => true;
    public override string HelpText => "CHMOD";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {

        if (context.Session.Account is not { IsAdmin: true })
        {
            await context.Session.WriteAsync("550 SITE CHMOD requires admin privileges.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync("501 Usage: SITE CHMOD <mode> <path>\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await context.Session.WriteAsync("501 Usage: SITE CHMOD <mode> <path>\r\n", cancellationToken);
            return;
        }

        var modeStr = parts[0];
        var pathArg = parts[1];

        if (!int.TryParse(modeStr, out var mode) || mode <= 0)
        {
            await context.Session.WriteAsync("501 Invalid mode.\r\n", cancellationToken);
            return;
        }

        var virt = FtpPath.Normalize(context.Session.Cwd, pathArg);
        string phys;
        try
        {
            phys = context.Router.FileSystem.MapToPhysical(virt);
        }
        catch
        {
            await context.Session.WriteAsync("550 Permission denied.\r\n", cancellationToken);
            return;
        }

        if (!File.Exists(phys) && !Directory.Exists(phys))
        {
            await context.Session.WriteAsync("550 File or directory not found.\r\n", cancellationToken);
            return;
        }

        try
        {
            // Very rough semantics:
            // If owner's write bit is 0 (e.g. 444), mark as ReadOnly.
            // If owner's write bit is 1 (e.g. 644, 755, 777), clear ReadOnly.
            var ownerWritable = ((mode / 10) % 10) >= 2; // second digit, >=2 => write

            if (File.Exists(phys))
            {
                var attrs = File.GetAttributes(phys);

                if (ownerWritable)
                    attrs &= ~FileAttributes.ReadOnly;
                else
                    attrs |= FileAttributes.ReadOnly;

                File.SetAttributes(phys, attrs);
            }
            else if (Directory.Exists(phys))
            {
                var attrs = File.GetAttributes(phys);

                if (ownerWritable)
                    attrs &= ~FileAttributes.ReadOnly;
                else
                    attrs |= FileAttributes.ReadOnly;

                File.SetAttributes(phys, attrs);
            }

            await context.Session.WriteAsync("200 CHMOD applied (best effort).\r\n", cancellationToken);
        }
        catch
        {
            await context.Session.WriteAsync("550 Failed to change mode.\r\n", cancellationToken);
        }

    }
}