/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRescanDupeCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 12:52:30
 *  Last Modified:  2025-12-14 21:35:46
 *  CRC32:          0x60CA3D3C
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

public sealed class SiteRescanDupeCommand : SiteCommandBase
{
    public override string Name => "RESCANDUPE";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText =>
        "RESCANDUPE <path> - rescan a release and show zipscript/dupe summary.";

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
            await s.WriteAsync("501 Usage: SITE RESCANDUPE <path>\r\n", cancellationToken);
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

        // Release: directory itself or parent directory of a file
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
            await s.WriteAsync("550 RESCANDUPE failed.\r\n", cancellationToken);
            return;
        }

        var files = status.Files;
        var ok = files.Count(f => f.State == ZipscriptFileState.Ok);
        var bad = files.Count(f => f.State == ZipscriptFileState.BadCrc);
        var missing = files.Count(f => f.State == ZipscriptFileState.Missing);
        var extra = files.Count(f => f.State == ZipscriptFileState.Extra);

        // Dupe info (best-effort)
        var dupeCount = 0;
        var anyNukedDupe = false;

        if (context.Runtime.DupeStore is { } dupeStore)
        {
            var trimmed = releaseVirt.TrimEnd('/', '\\');
            var releaseName = Path.GetFileName(trimmed);

            try
            {
                var matches = dupeStore.Search(releaseName);
                dupeCount = matches.Count;

                anyNukedDupe = matches.Any(e => e.IsNuked);
            }
            catch
            {
                // ignore dupe errors, keep zipscript result
            }
        }

        var line =
            $"250 RESCANDUPE {status.ReleasePath} " +
            $"SFV={(status.HasSfv ? "YES" : "NO")} " +
            $"COMPLETE={(status.IsComplete ? "YES" : "NO")} " +
            $"OK={ok} BAD={bad} MISSING={missing} EXTRA={extra} " +
            $"DUPES={dupeCount} NUKED_DUPES={(anyNukedDupe ? "YES" : "NO")}\r\n";

        await s.WriteAsync(line, cancellationToken);

        // Optional event
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
            Reason = $"RESCANDUPE complete={status.IsComplete}, ok={ok}, bad={bad}, missing={missing}, extra={extra}, dupes={dupeCount}, nukedDupes={anyNukedDupe}"
        });
    }
}