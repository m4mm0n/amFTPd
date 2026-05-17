/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SitePreInfoCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24 00:00:00
 *  Last Modified:  2026-04-24 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      SITE PREINFO <releasename>
 *      Displays detailed information about a live or pending pre entry:
 *      tagger, group, section, date, file count, size, tags, approval status.
 *      Also lists physical files if the virtual path is resolvable.
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
using amFTPd.Core.Pre;

namespace amFTPd.Core.Site.Commands;

public sealed class SitePreInfoCommand : SiteCommandBase
{
    public override string Name => "PREINFO";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText =>
        "PREINFO <releasename>  - show detailed info about a pre";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var runtime = context.Runtime;

        var releaseName = argument.Trim();
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            await s.WriteAsync("501 Syntax: SITE PREINFO <releasename>\r\n", cancellationToken);
            return;
        }

        // Look in live pres first
        var entry = runtime.PreRegistry.FindByReleaseName(releaseName);
        bool isPending = false;

        // If not found live, check pending queue (siteops only)
        if (entry is null && (s.Account?.IsSiteop ?? false))
        {
            if (runtime.PreRegistry.TryGetPending(releaseName, out var pendingEntry))
            {
                entry = pendingEntry;
                isPending = true;
            }
        }

        if (entry is null)
        {
            await s.WriteAsync($"550 No pre found for: {releaseName}\r\n", cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"200-Pre info: {entry.ReleaseName}");
        sb.AppendLine($" Section   : {entry.Section}");
        sb.AppendLine($" Tagged by : {entry.User}{(entry.Group is not null ? $" ({entry.Group})" : "")}");
        sb.AppendLine($" Date      : {entry.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($" Status    : {(isPending ? "PENDING APPROVAL" : entry.Status.ToString().ToUpper())}");

        if (entry.ReviewedBy is not null)
        {
            var action = entry.Status == PreStatus.Denied ? "Denied" : "Approved";
            sb.AppendLine($" {action,-9}: {entry.ReviewedBy} at {entry.ReviewedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
            if (entry.DenyReason is not null)
                sb.AppendLine($" Reason    : {entry.DenyReason}");
        }

        // File metadata
        if (entry.FileCount > 0 || entry.TotalBytes > 0)
        {
            var sizeMb = entry.TotalBytes / 1_048_576.0;
            sb.AppendLine($" Files     : {entry.FileCount}  ({sizeMb:F2} MB)");
        }

        if (!string.IsNullOrEmpty(entry.Tags))
            sb.AppendLine($" Tags      : {entry.Tags}");

        sb.AppendLine($" VPath     : {entry.VirtualPath}");

        // Attempt to enumerate the actual release directory
        if (!isPending)
        {
            try
            {
                var vfsResult = s.VfsManager?.Resolve(entry.VirtualPath, s.Account);
                if (vfsResult is { Success: true } &&
                    vfsResult.Node?.PhysicalPath is { } physDir
                    && Directory.Exists(physDir))
                {
                    var files = Directory
                        .GetFiles(physDir, "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(f => Path.GetFileName(f))
                        .ToList();

                    if (files.Count > 0)
                    {
                        sb.AppendLine(" Files on disk:");
                        foreach (var f in files)
                        {
                            var fi = new FileInfo(f);
                            var kb = fi.Length / 1024.0;
                            sb.AppendLine($"   {Path.GetFileName(f),-50} {kb,8:F1} KB");
                        }
                    }
                }
            }
            catch
            {
                // VFS enumeration is best-effort
            }
        }

        sb.AppendLine("200 End.");

        await s.WriteAsync(sb.ToString(), cancellationToken);
    }
}
