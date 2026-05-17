/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SitePreCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 05:02:06
 *  Last Modified:  2026-04-24 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      SITE PRE <section> <release>
 *      Registers a release as a pre in the dupe DB and pre registry.
 *      Enforces per-section AllowedPreGroups and RequirePreApproval.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Config.Ftpd;
using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Pre;
using amFTPd.Core.Sections;
using amFTPd.Core.Stats;

namespace amFTPd.Core.Site.Commands
{
    public sealed class SitePreCommand : SiteCommandBase
    {
        public override string Name => "PRE";

        // Often restricted to admins / prebot – tweak if you want
        public override bool RequiresAdmin => false;
        public override bool RequiresSiteop => false;

        public override string HelpText => "PRE <section> <release>  - register a pre in DUPE DB";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var s = context.Session;
            var runtime = context.Runtime;
            var dupeStore = runtime.DupeStore;

            if (dupeStore is null)
            {
                await s.WriteAsync("550 DUPE database not enabled.\r\n", cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(argument))
            {
                await s.WriteAsync("501 Usage: SITE PRE <section> <release>\r\n", cancellationToken);
                return;
            }

            var parts = argument.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await s.WriteAsync("501 Usage: SITE PRE <section> <release>\r\n", cancellationToken);
                return;
            }

            var sectionName = context.Sections.NormalizeSectionName(parts[0]);
            var releaseName = parts[1];
            var actor = s.Account?.UserName ?? "unknown";
            var group = s.Account?.GroupName;
            var isSiteop = s.Account?.IsSiteop ?? false;

            // ------------------------------------------------------------------
            // Per-section pre access control
            // ------------------------------------------------------------------
            var section = context.Sections.FindByNameOrAlias(sectionName);
            var requireApproval = section?.RequirePreApproval ?? false;

            if (!isSiteop && !requireApproval)
            {
                await s.WriteAsync("550 SITE PRE requires siteop privileges.\r\n", cancellationToken);
                return;
            }

            if (section is not null && section.AllowedPreGroups.Count > 0 && !isSiteop)
            {
                var allowed = group is not null &&
                              section.AllowedPreGroups.Any(g =>
                                  g.Equals(group, StringComparison.OrdinalIgnoreCase));
                if (!allowed)
                {
                    await s.WriteAsync(
                        $"550 Your group ({group ?? "none"}) is not allowed to PRE to section {sectionName}.\r\n",
                        cancellationToken);
                    return;
                }
            }

            // ------------------------------------------------------------------
            // Build virtual path and resolve file metadata from VFS (best effort)
            // ------------------------------------------------------------------
            var virtPath = $"/{sectionName}/{releaseName}".Replace('\\', '/');

            int fileCount = 0;
            long totalBytes = 0;

            try
            {
                if (s.VfsManager is not null)
                {
                    var vfsResult = s.VfsManager.Resolve(virtPath, s.Account);
                    if (vfsResult.Success && vfsResult.Node?.PhysicalPath is { } physDir
                        && Directory.Exists(physDir))
                    {
                        var files = Directory.GetFiles(physDir, "*", SearchOption.TopDirectoryOnly);
                        fileCount = files.Length;
                        totalBytes = files.Sum(f => new FileInfo(f).Length);
                    }
                }
            }
            catch
            {
                // metadata is best-effort; continue without it
            }

            // ------------------------------------------------------------------
            // Pre approval gate
            // ------------------------------------------------------------------
            var now = DateTimeOffset.UtcNow;
            var entry = new PreEntry(sectionName, releaseName, virtPath, actor, now)
            {
                Group = group,
                FileCount = fileCount,
                TotalBytes = totalBytes,
                Status = requireApproval ? PreStatus.Pending : PreStatus.Approved
            };

            if (requireApproval && !isSiteop)
            {
                // Add to pending queue; do NOT make it live yet
                if (!runtime.PreRegistry.EnqueuePending(entry))
                {
                    await s.WriteAsync(
                        $"550 A pending pre for \"{releaseName}\" already exists.\r\n",
                        cancellationToken);
                    return;
                }

                runtime.AuditLog?.Log(
                    actor: actor,
                    action: "PRE_PENDING",
                    target: releaseName,
                    detail: $"section={sectionName} group={group}",
                    ip: s.RemoteEndPoint?.Address.ToString());

                await s.WriteAsync(
                    $"200 PRE queued for approval: {sectionName} {releaseName}\r\n",
                    cancellationToken);
                return;
            }

            // ------------------------------------------------------------------
            // Immediate pre (approved = default, or siteop bypasses queue)
            // ------------------------------------------------------------------
            var existing = dupeStore.Find(sectionName, releaseName);
            DupeEntry dupeEntry;
            if (existing is null)
            {
                dupeEntry = new DupeEntry
                {
                    ReleaseName = releaseName,
                    SectionName = sectionName,
                    VirtualPath = virtPath,
                    TotalBytes = totalBytes,
                    FirstSeen = now,
                    LastUpdated = now,
                    UploaderUser = actor,
                    UploaderGroup = group,
                    IsNuked = false,
                    NukeReason = null,
                    NukeMultiplier = 0
                };
            }
            else
            {
                dupeEntry = existing with
                {
                    VirtualPath = virtPath,
                    LastUpdated = now
                };
            }

            dupeStore.Upsert(dupeEntry);
            runtime.PreRegistry.TryAdd(entry);
            context.SceneRegistry.MarkPre(sectionName, virtPath);
            PerfCounters.PreRegistered();

            // EventBus: announce PRE
            runtime.EventBus?.Publish(new FtpEvent
            {
                Type = FtpEventType.Pre,
                Timestamp = now,
                SessionId = s.SessionId,
                User = actor,
                Group = group,
                Section = sectionName,
                VirtualPath = virtPath,
                ReleaseName = releaseName,
                Extra = null
            });

            runtime.AuditLog?.Log(
                actor: actor,
                action: "PRE",
                target: releaseName,
                detail: $"section={sectionName} group={group} files={fileCount} size={totalBytes}",
                ip: s.RemoteEndPoint?.Address.ToString());

            await s.WriteAsync(
                $"200 PRE registered: {sectionName} {releaseName}\r\n",
                cancellationToken);
        }
    }
}
