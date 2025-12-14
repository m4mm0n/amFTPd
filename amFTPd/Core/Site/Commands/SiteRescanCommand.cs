/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRescanCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 09:00:57
 *  Last Modified:  2025-12-14 21:35:25
 *  CRC32:          0x709DEE17
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

public sealed class SiteRescanCommand : SiteCommandBase
{
    public override string Name => "RESCAN";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "RESCAN <path> - rescan a release (zipscript integration)";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        if (context.Runtime.Zipscript is null)
        {
            await context.Session.WriteAsync("550 Zipscript is not enabled.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync("501 Usage: SITE RESCAN <path>\r\n", cancellationToken);
            return;
        }

        var virtArg = FtpPath.Normalize(context.Session.Cwd, argument);

        string? phys;
        try
        {
            phys = context.Router.FileSystem.MapToPhysical(virtArg);
        }
        catch
        {
            await context.Session.WriteAsync("550 Permission denied.\r\n", cancellationToken);
            return;
        }

        var isDir = Directory.Exists(phys);
        var isFile = File.Exists(phys);

        if (!isDir && !isFile)
        {
            await context.Session.WriteAsync("550 File or directory not found.\r\n", cancellationToken);
            return;
        }

        // Determine release virtual+physical paths
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
            context.Session.Account?.UserName,
            IncludeSubdirs: false,
            RequestedAt: DateTimeOffset.UtcNow);

        var status = z.OnRescanDir(rescanCtx);

        if (status is null)
        {
            await context.Session.WriteAsync("550 RESCAN failed.\r\n", cancellationToken);
            return;
        }

        // Summarise status
        var files = status.Files;
        var ok = files.Count(f => f.State == ZipscriptFileState.Ok);
        var bad = files.Count(f => f.State == ZipscriptFileState.BadCrc);
        var missing = files.Count(f => f.State == ZipscriptFileState.Missing);
        var extra = files.Count(f => f.State == ZipscriptFileState.Extra);

        var line =
            $"250 RESCAN {status.ReleasePath} " +
            $"SFV={(status.HasSfv ? "YES" : "NO")} " +
            $"COMPLETE={(status.IsComplete ? "YES" : "NO")} " +
            $"OK={ok} BAD={bad} MISSING={missing} EXTRA={extra}\r\n";

        await context.Session.WriteAsync(line, cancellationToken);

        // Optionally push an event (ZipscriptStatus) to EventBus
        context.Runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.ZipscriptStatus,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = context.Session.SessionId,
            User = context.Session.Account?.UserName,
            Group = context.Session.Account?.GroupName,
            Section = status.SectionName,
            VirtualPath = status.ReleasePath,
            ReleaseName = Path.GetFileName(status.ReleasePath),
            Reason = $"RESCAN OK: complete={status.IsComplete}, ok={ok}, bad={bad}, missing={missing}, extra={extra}"
        });
    }
}