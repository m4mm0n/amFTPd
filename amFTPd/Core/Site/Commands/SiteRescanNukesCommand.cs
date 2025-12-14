/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRescanNukesCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 12:52:30
 *  Last Modified:  2025-12-14 21:36:07
 *  CRC32:          0x49AF62C4
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
using amFTPd.Core.Events;
using amFTPd.Core.Zipscript;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteRescanNukesCommand : SiteCommandBase
{
    public override string Name => "RESCANNUKES";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "RESCANNUKES <path> - rescan a release and show nuked/bad/missing summary.";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (context.Runtime.Zipscript is null)
        {
            await s.WriteAsync("550 Zipscript is not enabled.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Usage: SITE RESCANNUKES <path>\r\n", cancellationToken);
            return;
        }

        var virtArg = FtpPath.Normalize(s.Cwd, argument);

        string? phys;
        try
        {
            phys = context.Router.FileSystem.MapToPhysical(virtArg);
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

        string releaseVirt;
        string? releasePhys;

        if (isDir)
        {
            releaseVirt = virtArg;
            releasePhys = phys;
        }
        else
        {
            var dirVirtRaw = Path.GetDirectoryName(virtArg);
            releaseVirt = string.IsNullOrEmpty(dirVirtRaw) || dirVirtRaw == "\\"
                ? "/"
                : dirVirtRaw.Replace('\\', '/');

            releasePhys = Path.GetDirectoryName(phys)!;
        }

        var section = context.Router.GetSectionForVirtual(releaseVirt);
        var z = context.Runtime.Zipscript;

        var rescanCtx = new ZipscriptRescanContext(
            section.Name,
            releaseVirt,
            releasePhys,
            s.Account?.UserName,
            IncludeSubdirs: false,
            RequestedAt: DateTimeOffset.UtcNow);

        var status = z.OnRescanDir(rescanCtx);

        if (status is null)
        {
            await s.WriteAsync("550 RESCANNUKES failed.\r\n", cancellationToken);
            return;
        }

        var files = status.Files;
        var nukedFiles = files.Count(f => f.State == ZipscriptFileState.Nuked);
        var bad = files.Count(f => f.State == ZipscriptFileState.BadCrc);
        var missing = files.Count(f => f.State == ZipscriptFileState.Missing);

        var line =
            $"250 RESCANNUKES {status.ReleasePath} " +
            $"NUKED={(status.IsNuked ? "YES" : "NO")} " +
            $"WAS_NUKED={(status.WasNuked ? "YES" : "NO")} " +
            $"NUKED_FILES={nukedFiles} BAD={bad} MISSING={missing}\r\n";

        await s.WriteAsync(line, cancellationToken);

        context.Runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.ZipscriptStatus,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = context.Session.SessionId,
            User = s.Account?.UserName,
            Group = s.Account?.GroupName,
            Section = status.SectionName,
            VirtualPath = status.ReleasePath,
            ReleaseName = Path.GetFileName(status.ReleasePath),
            Reason = $"RESCANNUKES nuked={status.IsNuked}, wasNuked={status.WasNuked}, nukedFiles={nukedFiles}, bad={bad}, missing={missing}"
        });
    }
}