/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           GlIoImport.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 18:07:11
 *  Last Modified:  2026-05-02 00:00:00
 *  CRC32:          0x117FFCAC
 *  
 *  Description:
 *      Helper for importing data from glFTPD/ioFTPD-style databases into amFTPd.
 *      Includes full import for users, groups, PRE entries, nukes and dupes.
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
using amFTPd.Core.Import;
using amFTPd.Core.Import.Mappers;
using amFTPd.Core.Import.Parsers;
using amFTPd.Core.Import.Records;
using amFTPd.Core.Pre;
using amFTPd.Core.Sections;
using amFTPd.Core.Zipscript;
using amFTPd.Db;
using amFTPd.Logging;

namespace amFTPd.Utils.Tools
{
    public sealed record GlIoImportResult
    {
        public required ImportFlavor Flavor { get; init; }
        public required bool DryRun { get; init; }

        public required int ParsedGroups { get; init; }
        public required int ImportedGroups { get; init; }

        public required int ParsedUsers { get; init; }
        public required int ImportedUsers { get; init; }

        public required int ParsedPres { get; init; }
        public required int ImportedPres { get; init; }

        public required int ParsedNukes { get; init; }
        public required int ImportedNukes { get; init; }

        public required int ParsedDupes { get; init; }
        public required int ImportedDupes { get; init; }
        public required int UpdatedDupes { get; init; }
        public required int SkippedDupes { get; init; }
        public required int NukedDupes { get; init; }

        public required int UnknownSectionsCount { get; init; }
        public required IReadOnlyList<string> UnknownSections { get; init; }
    }

    /// <summary>
    /// Helper for importing data from glFTPD/ioFTPD-style databases into amFTPd.
    /// </summary>
    public static class GlIoImport
    {
        /// <summary>
        /// Compatibility method mirroring the previous contract:
        /// import data and return only completion status.
        /// </summary>
        public static async Task ImportAsync(
            string sourceRoot,
            SectionManager sections,
            DatabaseManager? db = null,
            IUserStore? users = null,
            IGroupStore? groups = null,
            CancellationToken cancellationToken = default)
        {
            await ImportWithSummaryAsync(
                sourceRoot,
                sections,
                db,
                users: users,
                groups: groups,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Imports users, groups, sections, stats and dupes from the given source root
        /// into the amFTPd database and configuration.
        /// </summary>
        public static async Task<GlIoImportResult> ImportWithSummaryAsync(
            string sourceRoot,
            SectionManager sections,
            DatabaseManager? db = null,
            bool dryRun = false,
            IFtpLogger? logger = null,
            ImportFlavor? flavor = null,
            PreRegistry? preRegistry = null,
            IDupeStore? dupeStore = null,
            ZipscriptEngine? zipscript = null,
            IUserStore? users = null,
            IGroupStore? groups = null,
            DupeImportMode dupeMode = DupeImportMode.Merge,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
                throw new ArgumentException("Source root must not be empty.", nameof(sourceRoot));

            if (sections is null)
                throw new ArgumentNullException(nameof(sections));

            if (!System.IO.Directory.Exists(sourceRoot))
                throw new System.IO.DirectoryNotFoundException(sourceRoot);

            var userStore = db?.Users ?? users;
            var groupStore = db?.Groups ?? groups;

            if (userStore is null)
                throw new ArgumentNullException(nameof(users), "A user store is required for import.");

            if (groupStore is null)
                throw new ArgumentNullException(nameof(groups), "A group store is required for import.");

            cancellationToken.ThrowIfCancellationRequested();

            var detectedFlavor = flavor ?? ImportFlavorDetector.Detect(sourceRoot);
            if (detectedFlavor == ImportFlavor.Unknown)
                throw new InvalidOperationException(
                    $"Unable to detect import flavor for '{sourceRoot}'. " +
                    "Use GLFTP/GLFTPD/IOFTPD markers or pass a flavor explicitly.");

            var isGl = detectedFlavor == ImportFlavor.GlFtpd;
            var unknownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? ResolveSection(string sourceSection)
            {
                if (string.IsNullOrWhiteSpace(sourceSection))
                    return null;

                var mapped = sections.FindByNameOrAlias(sourceSection.Trim());
                if (mapped is null)
                {
                    unknownSections.Add(sourceSection.Trim());
                    return null;
                }

                return mapped.Name;
            }

            static string NormalizeSectionPath(string sectionName, string sourcePath)
            {
                var clean = sourcePath.Replace('\\', '/').Trim();
                if (string.IsNullOrWhiteSpace(clean))
                    return $"/{sectionName}";

                var trimmed = clean.TrimStart('/');
                var idx = trimmed.IndexOf('/');
                if (idx < 0)
                    return $"/{sectionName}";

                var remainder = trimmed[(idx + 1)..];
                return string.IsNullOrWhiteSpace(remainder)
                    ? $"/{sectionName}"
                    : $"/{sectionName}/{remainder}";
            }

            // -------------------------------
            // GROUPS
            // -------------------------------
            IImportParser<ImportedGroupRecord> groupParser = isGl
                ? new GlGroupParser()
                : new IoGroupParser();

            var groupRecords = groupParser.Parse(sourceRoot).ToList();
            var importedGroups = groupRecords.Count(r => groupStore.FindGroup(r.GroupName) is null);

            if (!dryRun)
            {
                new GroupImportMapper().Apply(groupRecords, groupStore);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // -------------------------------
            // USERS
            // -------------------------------
            IImportParser<ImportedUserRecord> userParser = isGl
                ? new GlUserParser()
                : new IoUserParser();

            var userRecords = userParser.Parse(sourceRoot).ToList();
            var importedUsers = userRecords.Count(r => userStore.FindUser(r.UserName) is null);

            if (!dryRun)
            {
                new UserImportMapper().Apply(userRecords, userStore);
                new GroupMembershipReconciler().Apply(userStore, groupStore);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // -------------------------------
            // PRE
            // -------------------------------
            IImportParser<ImportedPreRecord> preParser = isGl
                ? new GlPreParser()
                : new IoPreParser();

            var preRecords = preParser.Parse(sourceRoot).ToList();
            var mappedPres = new List<ImportedPreRecord>(preRecords.Count);

            foreach (var p in preRecords)
            {
                var sectionName = ResolveSection(p.Section);
                if (sectionName is null)
                    continue;

                mappedPres.Add(
                    new ImportedPreRecord(
                        sectionName,
                        NormalizeSectionPath(sectionName, p.Path),
                        p.Group,
                        p.Timestamp));
            }

            var importedPres = 0;
            if (!dryRun && preRegistry is not null)
            {
                new PreImportMapper().Apply(mappedPres, preRegistry);
                importedPres = mappedPres.Count;
            }
            else if (!dryRun && preRegistry is null && mappedPres.Count > 0)
            {
                logger?.Log(
                    FtpLogLevel.Warn,
                    $"[IMPORT][PRE] Import skipped because no PreRegistry was supplied. " +
                    $"Detected {mappedPres.Count} mappable PRE record(s).");
            }
            else if (dryRun)
            {
                importedPres = mappedPres.Count;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // -------------------------------
            // NUKE
            // -------------------------------
            IImportParser<ImportedNukeRecord> nukeParser = isGl
                ? new GlNukeParser()
                : new IoNukeParser();

            var nukeRecords = nukeParser.Parse(sourceRoot).ToList();
            var mappedNukes = new List<ImportedNukeRecord>(nukeRecords.Count);

            foreach (var n in nukeRecords)
            {
                var sectionName = ResolveSection(n.Section);
                if (sectionName is null)
                    continue;

                mappedNukes.Add(new ImportedNukeRecord(
                    sectionName,
                    NormalizeSectionPath(sectionName, n.Path),
                    n.Multiplier,
                    n.Reason,
                    n.Nuker,
                    n.Timestamp));
            }

            var importedNukes = 0;
            if (!dryRun && zipscript is not null)
            {
                foreach (var n in mappedNukes)
                {
                    zipscript.MarkReleaseNuked(
                        n.Path,
                        n.Section,
                        n.Nuker,
                        n.Reason,
                        n.Multiplier);
                }
            }

            if (!dryRun && zipscript is null && mappedNukes.Count > 0)
            {
                logger?.Log(
                    FtpLogLevel.Warn,
                    $"[IMPORT][NUKE] Import skipped because no ZipscriptEngine was supplied. " +
                    $"Detected {mappedNukes.Count} mappable NUKE record(s).");
            }
            else if (dryRun)
            {
                importedNukes = mappedNukes.Count;
            }
            else
            {
                importedNukes = mappedNukes.Count;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // -------------------------------
            // DUPE
            // -------------------------------
            IImportParser<ImportedDupeRecord> dupeParser = isGl
                ? new GlDupeParser()
                : new IoDupeParser();

            var dupeRecords = dupeParser.Parse(sourceRoot).ToList();
            var mappedDupes = new List<ImportedDupeRecord>(dupeRecords.Count);

            foreach (var d in dupeRecords)
            {
                var sectionName = ResolveSection(d.Section);
                if (sectionName is null)
                    continue;

                mappedDupes.Add(new ImportedDupeRecord
                {
                    Section = sectionName,
                    Release = d.Release,
                    Group = d.Group,
                    FirstSeen = d.FirstSeen,
                    TotalBytes = d.TotalBytes,
                    IsNuked = d.IsNuked,
                    NukeReason = d.NukeReason,
                    NukeMultiplier = d.NukeMultiplier
                });
            }

            var dupeStats = new DupeImportStats
            {
                Total = mappedDupes.Count
            };

            if (dupeStore is not null)
            {
                dupeStats = new DupeImportMapper().Apply(
                    mappedDupes,
                    dupeStore,
                    dupeMode,
                    dryRun);
            }
            else if (dryRun && mappedDupes.Count > 0)
            {
                logger?.Log(
                    FtpLogLevel.Info,
                    $"[IMPORT][DUPE] Dry-run preview for {mappedDupes.Count} mappable DUPE record(s) skipped " +
                    "because no dupe store was supplied.");
            }
            else if (!dryRun && dupeStore is null && mappedDupes.Count > 0)
            {
                logger?.Log(
                    FtpLogLevel.Warn,
                    $"[IMPORT][DUPE] Import skipped because no dupe store was supplied. " +
                    $"Detected {mappedDupes.Count} mappable DUPE record(s).");
            }

            if (!dryRun && dupeStore is not null && mappedNukes.Count > 0)
            {
                ApplyNukeStateToDupeStore(mappedNukes, dupeStore);
            }

            await Task.CompletedTask;

            return new GlIoImportResult
            {
                Flavor = detectedFlavor,
                DryRun = dryRun,
                ParsedGroups = groupRecords.Count,
                ImportedGroups = importedGroups,
                ParsedUsers = userRecords.Count,
                ImportedUsers = importedUsers,
                ParsedPres = preRecords.Count,
                ImportedPres = importedPres,
                ParsedNukes = nukeRecords.Count,
                ImportedNukes = importedNukes,
                ParsedDupes = dupeRecords.Count,
                ImportedDupes = dupeStats.Inserted,
                UpdatedDupes = dupeStats.Updated,
                SkippedDupes = dupeStats.Skipped,
                NukedDupes = dupeStats.Nuked,
                UnknownSectionsCount = unknownSections.Count,
                UnknownSections = unknownSections.OrderBy(s => s).ToList()
            };
        }

        private static void ApplyNukeStateToDupeStore(
            IReadOnlyList<ImportedNukeRecord> nukeRecords,
            IDupeStore dupeStore)
        {
            foreach (var n in nukeRecords)
            {
                var releaseName = Path.GetFileName(n.Path.TrimEnd('/', '\\'));
                if (string.IsNullOrWhiteSpace(releaseName))
                    continue;

                var existing = dupeStore.Find(n.Section, releaseName);
                if (existing is null)
                    continue;

                var merged = existing with
                {
                    IsNuked = true,
                    NukeReason = existing.IsNuked && !string.IsNullOrWhiteSpace(existing.NukeReason)
                        ? existing.NukeReason
                        : n.Reason,
                    NukeMultiplier = existing.NukeMultiplier > 0
                        ? Math.Max(existing.NukeMultiplier, n.Multiplier)
                        : n.Multiplier,
                    LastUpdated = n.Timestamp
                };

                dupeStore.Upsert(merged);
            }
        }
    }
}
