/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           MigrationReport.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Structured, JSON-serializable output of the migration validation tool
 *      (--check-migration). Contains per-category findings and a top-level summary.
 *
 *  License:
 *      MIT License — https://opensource.org/licenses/MIT
 * ====================================================================================================
 */

using System.Text.Json.Serialization;

namespace amFTPd.Core.Migration;

// ┌─────────────────────────────────────────────────────────────────────────────┐
// │  Top-level report                                                           │
// └─────────────────────────────────────────────────────────────────────────────┘

/// <summary>
/// Full output of a single migration validation run.
/// Serialize this object to JSON for machine consumption;
/// use <see cref="MigrationReportFormatter"/> for human-readable output.
/// </summary>
public sealed class MigrationReport
{
    /// <summary>UTC timestamp when the validation was run.</summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Detected source flavor (e.g. "ioFTPD", "glFTPD", "Unknown").</summary>
    public string SourceFlavor { get; init; } = "Unknown";

    /// <summary>Absolute path to the source directory that was inspected.</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Absolute path to the amftpd.json configuration file used.</summary>
    public string ConfigPath { get; init; } = string.Empty;

    /// <summary>
    /// Overall validation status.
    /// "Ok" if all counts match, "Warnings" if minor discrepancies exist,
    /// "Errors" if data loss is detected.
    /// </summary>
    public string Status { get; set; } = "Ok";

    /// <summary>Human-readable one-line summary (same as console header line).</summary>
    public string Summary { get; set; } = string.Empty;

    // ── Per-category reports ──────────────────────────────────────────────

    public UserMigrationReport Users { get; init; } = new();
    public GroupMigrationReport Groups { get; init; } = new();
    public DupeMigrationReport Dupes { get; init; } = new();
    public NukeMigrationReport Nukes { get; init; } = new();
}

// ┌─────────────────────────────────────────────────────────────────────────────┐
// │  Per-category reports                                                       │
// └─────────────────────────────────────────────────────────────────────────────┘

/// <summary>
/// Comparison of user accounts between the source FTP server and amFTPd.
/// </summary>
public sealed class UserMigrationReport
{
    /// <summary>Number of users parsed from the source data.</summary>
    public int SourceCount { get; set; }

    /// <summary>Number of users present in the amFTPd user store.</summary>
    public int TargetCount { get; set; }

    /// <summary>
    /// Users that exist in the source but are absent in amFTPd.
    /// A non-empty list indicates likely data loss.
    /// </summary>
    public List<string> MissingInTarget { get; init; } = [];

    /// <summary>
    /// Users whose credit total (KB) differs significantly between source and amFTPd.
    /// Keyed by username, value is <c>(sourceKb, targetKb)</c>.
    /// </summary>
    public List<CreditMismatch> CreditMismatches { get; init; } = [];

    [JsonIgnore]
    public bool HasIssues =>
        MissingInTarget.Count > 0 || CreditMismatches.Count > 0;
}

/// <summary>A single user whose credit total differs between source and target.</summary>
public sealed class CreditMismatch
{
    public string Username { get; init; } = string.Empty;
    public long SourceKb { get; init; }
    public long TargetKb { get; init; }
    public long DeltaKb => TargetKb - SourceKb;
}

/// <summary>
/// Comparison of groups between the source FTP server and amFTPd.
/// </summary>
public sealed class GroupMigrationReport
{
    /// <summary>Number of groups parsed from the source data.</summary>
    public int SourceCount { get; set; }

    /// <summary>Number of groups present in the amFTPd group store (0 if store is unavailable).</summary>
    public int TargetCount { get; set; }

    /// <summary>Group names that exist in the source but are absent in amFTPd.</summary>
    public List<string> MissingInTarget { get; init; } = [];

    [JsonIgnore]
    public bool HasIssues => MissingInTarget.Count > 0;
}

/// <summary>
/// Comparison of dupe-store entries between the source FTP server and amFTPd.
/// </summary>
public sealed class DupeMigrationReport
{
    /// <summary>Number of dupe entries parsed from the source data.</summary>
    public int SourceCount { get; set; }

    /// <summary>Number of entries in the amFTPd dupe store (0 if unavailable).</summary>
    public int TargetCount { get; set; }

    /// <summary>
    /// Releases present in the source but absent in the amFTPd dupe store.
    /// Only the first <see cref="MigrationValidator.MaxMissingDupeSamples"/> are listed.
    /// </summary>
    public List<MissingDupeEntry> MissingInTarget { get; init; } = [];

    /// <summary>
    /// Total count of releases missing from the amFTPd dupe store.
    /// May exceed <see cref="MissingInTarget"/>.<c>Count</c>.
    /// </summary>
    public int MissingCount { get; set; }

    /// <summary>
    /// Number of dupe rows skipped during validation because the source section
    /// could not be resolved to a configured amFTPd section.
    /// </summary>
    public int UnknownSectionRecordCount { get; set; }

    /// <summary>
    /// Distinct section names from source data that did not resolve to configured sections.
    /// </summary>
    public List<string> UnknownSections { get; set; } = [];

    [JsonIgnore]
    public bool HasIssues => MissingCount > 0;

    [JsonIgnore]
    public bool HasSectionResolutionIssues => UnknownSectionRecordCount > 0;
}

/// <summary>A release that exists in the source but not in the amFTPd dupe store.</summary>
public sealed class MissingDupeEntry
{
    public string Section { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
}

/// <summary>
/// Comparison of nuked releases between the source and the amFTPd dupe store.
/// </summary>
public sealed class NukeMigrationReport
{
    /// <summary>Number of nuke records parsed from the source data.</summary>
    public int SourceCount { get; set; }

    /// <summary>Number of entries in the amFTPd dupe store that have IsNuked = true.</summary>
    public int TargetNukedCount { get; set; }

    /// <summary>
    /// Nuked releases from the source whose dupe entry in amFTPd does NOT have IsNuked set.
    /// Only the first <see cref="MigrationValidator.MaxMissingNukeSamples"/> are listed.
    /// </summary>
    public List<MissingNukeEntry> MissingNukeFlag { get; init; } = [];

    /// <summary>Total count of nukes whose flag is missing in amFTPd (may exceed <c>MissingNukeFlag.Count</c>).</summary>
    public int MissingNukeFlagCount { get; set; }

    /// <summary>
    /// Number of nuke rows skipped during validation because the source section
    /// could not be resolved to a configured amFTPd section.
    /// </summary>
    public int UnknownSectionRecordCount { get; set; }

    /// <summary>
    /// Distinct section names from source data that did not resolve to configured sections.
    /// </summary>
    public List<string> UnknownSections { get; set; } = [];

    [JsonIgnore]
    public bool HasIssues => MissingNukeFlagCount > 0;

    [JsonIgnore]
    public bool HasSectionResolutionIssues => UnknownSectionRecordCount > 0;
}

/// <summary>A nuke record from the source whose dupe entry in amFTPd is not flagged as nuked.</summary>
public sealed class MissingNukeEntry
{
    public string Section { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int Multiplier { get; init; }
}
