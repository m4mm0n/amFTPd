/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRescanStatsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 12:52:30
 *  Last Modified:  2025-12-14 21:36:35
 *  CRC32:          0x850B6D2C
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

public sealed class SiteRescanStatsCommand : SiteCommandBase
{
    public override string Name => "RESCANSTATS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "RESCANSTATS <path> - rescan a release and show detailed zipscript stats.";

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
            await s.WriteAsync("501 Usage: SITE RESCANSTATS <path>\r\n", cancellationToken);
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
            await s.WriteAsync("550 RESCANSTATS failed.\r\n", cancellationToken);
            return;
        }

        var files = status.Files;
        var ok = files.Count(f => f.State == ZipscriptFileState.Ok);
        var bad = files.Count(f => f.State == ZipscriptFileState.BadCrc);
        var missing = files.Count(f => f.State == ZipscriptFileState.Missing);
        var extra = files.Count(f => f.State == ZipscriptFileState.Extra);
        var nuked = files.Count(f => f.State == ZipscriptFileState.Nuked);

        await s.WriteAsync(
            $"250 RESCANSTATS {status.ReleasePath}\r\n", cancellationToken);
        await s.WriteAsync(
            $"    SECTION: {status.SectionName}\r\n", cancellationToken);
        await s.WriteAsync(
            $"    SFV: {(status.HasSfv ? "YES" : "NO")}\r\n", cancellationToken);
        await s.WriteAsync(
            $"    COMPLETE: {(status.IsComplete ? "YES" : "NO")}\r\n", cancellationToken);
        await s.WriteAsync(
            $"    NUKED: {(status.IsNuked ? "YES" : "NO")} (WAS_NUKED: {(status.WasNuked ? "YES" : "NO")})\r\n",
            cancellationToken);

        if (!string.IsNullOrEmpty(status.NukeReason))
        {
            await s.WriteAsync(
                $"    NUKE_REASON: {status.NukeReason}\r\n", cancellationToken);
        }

        if (!string.IsNullOrEmpty(status.NukedBy))
        {
            await s.WriteAsync(
                $"    NUKED_BY: {status.NukedBy}\r\n", cancellationToken);
        }

        if (status.NukeMultiplier is > 0)
        {
            await s.WriteAsync(
                $"    NUKE_MULT: {status.NukeMultiplier:0.0}x\r\n", cancellationToken);
        }

        await s.WriteAsync(
            $"    FILES: OK={ok} BAD={bad} MISSING={missing} EXTRA={extra} NUKED={nuked}\r\n",
            cancellationToken);

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
            Reason = $"RESCANSTATS complete={status.IsComplete}, ok={ok}, bad={bad}, missing={missing}, extra={extra}, nuked={nuked}"
        });
    }
}