/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AutoNukeRule.cs
 *  Created:        2026-04-24
 *  Description:    Configuration model for automated nuke rules.
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

namespace amFTPd.Core.AutoNuke;

/// <summary>
/// The condition that triggers an automatic nuke.
/// </summary>
public enum AutoNukeTrigger
{
    /// <summary>One or more SFV-listed files have a CRC mismatch (immediate).</summary>
    BadCrc,

    /// <summary>Release is complete but contains no .nfo file after the grace period.</summary>
    MissingNfo,

    /// <summary>Release has no .sfv file after the grace period.</summary>
    MissingSfv,

    /// <summary>Release is still incomplete after the grace period.</summary>
    Incomplete,
}

/// <summary>
/// A single auto-nuke rule.
/// </summary>
public sealed record AutoNukeRule
{
    /// <summary>Friendly name shown in nuke reason (e.g. "BAD-CRC").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Section this rule applies to. Use "*" (or leave empty) to match all sections.
    /// </summary>
    public string Section { get; init; } = "*";

    /// <summary>What triggers this rule.</summary>
    public AutoNukeTrigger Trigger { get; init; }

    /// <summary>
    /// How long to wait after a release completes before evaluating the rule.
    /// Relevant for <see cref="AutoNukeTrigger.MissingNfo"/>, <see cref="AutoNukeTrigger.MissingSfv"/>,
    /// and <see cref="AutoNukeTrigger.Incomplete"/>.
    /// Default: zero (immediate evaluation on completion).
    /// </summary>
    public TimeSpan GracePeriod { get; init; } = TimeSpan.Zero;

    /// <summary>Nuke reason string written to the release/log.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Nuke multiplier (default 3×).</summary>
    public double NukeMultiplier { get; init; } = 3.0;

    /// <summary>Whether the rule is enabled.</summary>
    public bool Enabled { get; init; } = true;

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>True if this rule applies to the given section name.</summary>
    public bool AppliesToSection(string sectionName) =>
        string.IsNullOrWhiteSpace(Section) ||
        Section == "*" ||
        Section.Equals(sectionName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Effective reason string, falling back to the trigger name.</summary>
    public string EffectiveReason =>
        string.IsNullOrWhiteSpace(Reason) ? Trigger.ToString().ToUpperInvariant() : Reason;
}
