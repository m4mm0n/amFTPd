/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           MigrationValidator.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Compares a legacy FTP server data directory (ioFTPD or glFTPD) against a
 *      running amFTPd configuration and produces a structured MigrationReport.
 *
 *      Comparison categories:
 *        • Users   — count, missing accounts, credit totals
 *        • Groups  — count, missing groups
 *        • Dupes   — count, sampled missing releases
 *        • Nukes   — count, sampled releases whose nuke flag is absent in amFTPd
 *
 *  License:
 *      MIT License — https://opensource.org/licenses/MIT
 * ====================================================================================================
 */

using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Dupe;
using amFTPd.Core.Import;
using amFTPd.Core.Import.Parsers;
using amFTPd.Core.Import.Records;
using amFTPd.Core.Sections;
using amFTPd.Logging;

namespace amFTPd.Core.Migration;

/// <summary>
/// Validates that a completed migration from ioFTPD or glFTPD is consistent with the
/// current amFTPd state. Returns a <see cref="MigrationReport"/> that can be serialised to
/// JSON or printed in human-readable form.
/// </summary>
public static class MigrationValidator
{
    /// <summary>Maximum number of missing dupe samples included in the report.</summary>
    public const int MaxMissingDupeSamples = 50;

    /// <summary>Maximum number of missing nuke-flag samples included in the report.</summary>
    public const int MaxMissingNukeSamples = 50;

    /// <summary>
    /// Tolerance (in KB) for flagging a credit mismatch — small rounding differences are
    /// ignored.  Credits below this delta on either side are not reported.
    /// </summary>
    public const long CreditToleranceKb = 1024; // 1 MB

    // ── Public entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Run all validation checks and return a full <see cref="MigrationReport"/>.
    /// </summary>
    /// <param name="sourcePath">
    /// Directory containing the original ioFTPD or glFTPD installation
    /// (auto-detected via <see cref="ImportFlavorDetector"/>).
    /// </param>
    /// <param name="runtime">
    /// Fully loaded amFTPd runtime configuration to validate against.
    /// </param>
    /// <param name="logger">Optional logger for progress / warnings.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<MigrationReport> ValidateAsync(
        string sourcePath,
        AmFtpdRuntimeConfig runtime,
        IFtpLogger? logger = null,
        CancellationToken ct = default)
    {
        var flavor = ImportFlavorDetector.Detect(sourcePath);

        var report = new MigrationReport
        {
            SourceFlavor = flavor.ToString(),
            SourcePath = Path.GetFullPath(sourcePath),
            ConfigPath = runtime.ConfigFilePath,
        };

        logger?.Log(FtpLogLevel.Info,
            $"[Migration] Validating {flavor} import from '{sourcePath}'…");

        // ── Users ──────────────────────────────────────────────────────────────
        ct.ThrowIfCancellationRequested();
        ValidateUsers(sourcePath, flavor, runtime, report.Users, logger);

        // ── Groups ─────────────────────────────────────────────────────────────
        ct.ThrowIfCancellationRequested();
        ValidateGroups(sourcePath, flavor, runtime, report.Groups, logger);

        // ── Dupes ──────────────────────────────────────────────────────────────
        ct.ThrowIfCancellationRequested();
        if (runtime.DupeStore is not null)
            ValidateDupes(
                sourcePath,
                flavor,
                runtime.Sections,
                runtime.DupeStore,
                report.Dupes,
                logger);
        else
            logger?.Log(FtpLogLevel.Warn,
                "[Migration] No dupe store configured — skipping dupe validation.");

        // ── Nukes ──────────────────────────────────────────────────────────────
        ct.ThrowIfCancellationRequested();
        if (runtime.DupeStore is not null)
            ValidateNukes(
                sourcePath,
                flavor,
                runtime.Sections,
                runtime.DupeStore,
                report.Nukes,
                logger);

        // ── Overall status ─────────────────────────────────────────────────────
        SetOverallStatus(report);

        logger?.Log(FtpLogLevel.Info,
            $"[Migration] Validation complete. Status: {report.Status}. {report.Summary}");

        return Task.FromResult(report);
    }

    // ── Users ──────────────────────────────────────────────────────────────────

    private static void ValidateUsers(
        string sourcePath, ImportFlavor flavor,
        AmFtpdRuntimeConfig runtime,
        UserMigrationReport r, IFtpLogger? log)
    {
        IEnumerable<ImportedUserRecord> sourceUsers;
        try
        {
            sourceUsers = flavor == ImportFlavor.GlFtpd
                ? new GlUserParser().Parse(sourcePath).ToList()
                : new IoUserParser().Parse(sourcePath).ToList();
        }
        catch (Exception ex)
        {
            log?.Log(FtpLogLevel.Warn,
                $"[Migration][Users] Could not parse source: {ex.Message}", ex);
            return;
        }

        r.SourceCount = sourceUsers.Count();

        // amFTPd target
        var targetUsers = runtime.UserStore.GetAllUsers()
            .ToDictionary(u => u.UserName, u => u, StringComparer.OrdinalIgnoreCase);

        r.TargetCount = targetUsers.Count;

        foreach (var su in sourceUsers)
        {
            if (!targetUsers.TryGetValue(su.UserName, out var tu))
            {
                r.MissingInTarget.Add(su.UserName);
                continue;
            }

            var delta = Math.Abs(tu.CreditsKb - su.CreditsKb);
            if (delta > CreditToleranceKb)
            {
                r.CreditMismatches.Add(new CreditMismatch
                {
                    Username = su.UserName,
                    SourceKb = su.CreditsKb,
                    TargetKb = tu.CreditsKb
                });
            }
        }

        log?.Log(FtpLogLevel.Info,
            $"[Migration][Users] source={r.SourceCount} target={r.TargetCount} " +
            $"missing={r.MissingInTarget.Count} creditMismatches={r.CreditMismatches.Count}");
    }

    // ── Groups ─────────────────────────────────────────────────────────────────

    private static void ValidateGroups(
        string sourcePath, ImportFlavor flavor,
        AmFtpdRuntimeConfig runtime,
        GroupMigrationReport r, IFtpLogger? log)
    {
        IEnumerable<ImportedGroupRecord> sourceGroups;
        try
        {
            sourceGroups = flavor == ImportFlavor.GlFtpd
                ? new GlGroupParser().Parse(sourcePath).ToList()
                : new IoGroupParser().Parse(sourcePath).ToList();
        }
        catch (Exception ex)
        {
            log?.Log(FtpLogLevel.Warn,
                $"[Migration][Groups] Could not parse source: {ex.Message}", ex);
            return;
        }

        r.SourceCount = sourceGroups.Count();

        // Build a set of known group names in amFTPd.
        // If a dedicated group store is available, use it; otherwise fall back to
        // deriving groups from user PrimaryGroup / SecondaryGroups.
        HashSet<string> targetGroupNames;

        if (runtime.GroupStore is not null)
        {
            targetGroupNames = new HashSet<string>(
                runtime.GroupStore.GetAllGroups().Select(g => g.GroupName),
                StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            // JSON/config backend: derive groups from user accounts
            targetGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in runtime.UserStore.GetAllUsers())
            {
                if (!string.IsNullOrWhiteSpace(u.PrimaryGroup))
                    targetGroupNames.Add(u.PrimaryGroup);
                foreach (var sg in u.SecondaryGroups ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(sg))
                        targetGroupNames.Add(sg);
                }
            }
        }

        r.TargetCount = targetGroupNames.Count;

        foreach (var sg in sourceGroups)
        {
            if (!targetGroupNames.Contains(sg.GroupName))
                r.MissingInTarget.Add(sg.GroupName);
        }

        log?.Log(FtpLogLevel.Info,
            $"[Migration][Groups] source={r.SourceCount} target={r.TargetCount} " +
            $"missing={r.MissingInTarget.Count}");
    }

    // ── Dupes ──────────────────────────────────────────────────────────────────

    private static void ValidateDupes(
        string sourcePath,
        ImportFlavor flavor,
        SectionManager sectionManager,
        IDupeStore dupeStore,
        DupeMigrationReport r, IFtpLogger? log)
    {
        var unknownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownSectionRecords = 0;

        IEnumerable<ImportedDupeRecord> sourceDupes;
        try
        {
            sourceDupes = flavor == ImportFlavor.GlFtpd
                ? new GlDupeParser().Parse(sourcePath).ToList()
                : new IoDupeParser().Parse(sourcePath).ToList();
        }
        catch (Exception ex)
        {
            log?.Log(FtpLogLevel.Warn,
                $"[Migration][Dupes] Could not parse source: {ex.Message}", ex);
            return;
        }

        r.SourceCount = sourceDupes.Count();

        // Build a lookup set of all keys in the amFTPd dupe store.
        var targetKeys = dupeStore.GetAll()
            .Select(e => DupeEntry.MakeKey(e.SectionName, e.ReleaseName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        r.TargetCount = targetKeys.Count;

        foreach (var sd in sourceDupes)
        {
            var section = ResolveSectionForValidation(sd.Section, sectionManager);
            if (section is null)
            {
                unknownSections.Add(sd.Section);
                unknownSectionRecords++;
                continue;
            }

            var key = DupeEntry.MakeKey(section, sd.Release);
            if (!targetKeys.Contains(key))
            {
                r.MissingCount++;
                if (r.MissingInTarget.Count < MaxMissingDupeSamples)
                {
                    r.MissingInTarget.Add(new MissingDupeEntry
                    {
                        Section = section,
                        ReleaseName = sd.Release,
                        Group = sd.Group
                    });
                }
            }
        }

        if (unknownSections.Count > 0)
        {
            log?.Log(
                FtpLogLevel.Warn,
                $"[Migration][Dupes] Skipped {unknownSectionRecords} dupe record(s) from unknown section(s): " +
                string.Join(", ", unknownSections.OrderBy(s => s)));
        }

        r.UnknownSectionRecordCount = unknownSectionRecords;
        r.UnknownSections = unknownSections.OrderBy(s => s).ToList();

        log?.Log(FtpLogLevel.Info,
            $"[Migration][Dupes] source={r.SourceCount} target={r.TargetCount} " +
            $"missing={r.MissingCount}");
    }

    // ── Nukes ──────────────────────────────────────────────────────────────────

    private static void ValidateNukes(
        string sourcePath,
        ImportFlavor flavor,
        SectionManager sectionManager,
        IDupeStore dupeStore,
        NukeMigrationReport r, IFtpLogger? log)
    {
        var unknownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownSectionRecords = 0;

        IEnumerable<ImportedNukeRecord> sourceNukes;
        try
        {
            sourceNukes = flavor == ImportFlavor.GlFtpd
                ? new GlNukeParser().Parse(sourcePath).ToList()
                : new IoNukeParser().Parse(sourcePath).ToList();
        }
        catch (Exception ex)
        {
            log?.Log(FtpLogLevel.Warn,
                $"[Migration][Nukes] Could not parse source: {ex.Message}", ex);
            return;
        }

        r.SourceCount = sourceNukes.Count();

        // Build a lookup of nuked entries in the amFTPd dupe store.
        var allDupes = dupeStore.GetAll()
            .ToDictionary(e => DupeEntry.MakeKey(e.SectionName, e.ReleaseName),
                          e => e,
                          StringComparer.OrdinalIgnoreCase);

        r.TargetNukedCount = allDupes.Values.Count(e => e.IsNuked);

        // For each source nuke record, derive the release name from the path.
        // ioFTPD/glFTPD nuke records carry a virtual path like "/MP3/Release-Name".
        // We extract the last path segment as the release name.
        foreach (var sn in sourceNukes)
        {
            var releaseName = Path.GetFileName(sn.Path.TrimEnd('/', '\\'));
            if (string.IsNullOrWhiteSpace(releaseName))
                continue;

            var section = ResolveSectionForValidation(
                sn.Section,
                sectionManager);
            if (section is null)
            {
                unknownSections.Add(sn.Section);
                unknownSectionRecords++;
                continue;
            }

            var key = DupeEntry.MakeKey(section, releaseName);

            // Check if the release exists in the dupe store with IsNuked = true.
            if (!allDupes.TryGetValue(key, out var entry) || !entry.IsNuked)
            {
                r.MissingNukeFlagCount++;
                if (r.MissingNukeFlag.Count < MaxMissingNukeSamples)
                {
                    r.MissingNukeFlag.Add(new MissingNukeEntry
                    {
                        Section = section,
                        Path = sn.Path,
                        Reason = sn.Reason,
                        Multiplier = sn.Multiplier
                    });
                }
            }
        }

        if (unknownSections.Count > 0)
        {
            log?.Log(
                FtpLogLevel.Warn,
                $"[Migration][Nukes] Skipped {unknownSectionRecords} nuke record(s) from unknown section(s): " +
                string.Join(", ", unknownSections.OrderBy(s => s)));
        }

        r.UnknownSectionRecordCount = unknownSectionRecords;
        r.UnknownSections = unknownSections.OrderBy(s => s).ToList();

        log?.Log(FtpLogLevel.Info,
            $"[Migration][Nukes] source={r.SourceCount} targetNuked={r.TargetNukedCount} " +
            $"missingNukeFlag={r.MissingNukeFlagCount}");
    }

    // ── Overall status ─────────────────────────────────────────────────────────

    private static void SetOverallStatus(MigrationReport report)
    {
        bool hasErrors = report.Users.MissingInTarget.Count > 0
                        || report.Groups.MissingInTarget.Count > 0;

        bool hasWarnings = report.Users.CreditMismatches.Count > 0
                        || report.Dupes.MissingCount > 0
                        || report.Dupes.UnknownSectionRecordCount > 0
                        || report.Nukes.MissingNukeFlagCount > 0
                        || report.Nukes.UnknownSectionRecordCount > 0;

        if (hasErrors)
        {
            report.Status = "Errors";
            report.Summary = BuildSummary(report, "DATA LOSS DETECTED");
        }
        else if (hasWarnings)
        {
            report.Status = "Warnings";
            report.Summary = BuildSummary(report, "warnings present");
        }
        else
        {
            report.Status = "Ok";
            report.Summary = BuildSummary(report, "all counts match");
        }
    }

    private static string BuildSummary(MigrationReport r, string headline)
        => $"{r.SourceFlavor} → amFTPd [{headline}] — " +
           $"users: {r.Users.TargetCount}/{r.Users.SourceCount} " +
           $"(missing {r.Users.MissingInTarget.Count}), " +
           $"groups: {r.Groups.TargetCount}/{r.Groups.SourceCount} " +
           $"(missing {r.Groups.MissingInTarget.Count}), " +
           $"dupes: {r.Dupes.TargetCount}/{r.Dupes.SourceCount} " +
           $"(missing {r.Dupes.MissingCount}, unknown-section rows {r.Dupes.UnknownSectionRecordCount}), " +
           $"nukes: {r.Nukes.TargetNukedCount}/{r.Nukes.SourceCount} " +
           $"(unflagged {r.Nukes.MissingNukeFlagCount}, unknown-section rows {r.Nukes.UnknownSectionRecordCount})";

    private static string? ResolveSectionForValidation(
        string sourceSection,
        SectionManager sectionResolver)
    {
        if (string.IsNullOrWhiteSpace(sourceSection))
            return null;

        var canonicalCandidate = sourceSection.Trim();
        if (canonicalCandidate.StartsWith('/'))
            canonicalCandidate = canonicalCandidate.Trim('/');
        var mapped = sectionResolver.FindByNameOrAlias(canonicalCandidate);
        return mapped is null ? null : mapped.Name;
    }
}
