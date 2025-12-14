/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptReleaseStatus.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 04:38:40
 *  Last Modified:  2025-12-14 10:54:25
 *  CRC32:          0xDD9DE1C0
 *  
 *  Description:
 *      Represents the status of a release: virtual path, section, completion and nuking state, plus all files in that release.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


namespace amFTPd.Core.Zipscript;

/// <summary>
/// Represents the status of a release: virtual path, section, completion and nuking state,
/// plus all files in that release.
/// </summary>
public sealed class ZipscriptReleaseStatus
{
    /// <summary>Virtual path to the release directory (e.g. /MP3/Some-Release).</summary>
    public string ReleasePath { get; init; } = string.Empty;

    public string SectionName { get; init; } = string.Empty;

    /// <summary>True if an .sfv file has been seen in the release.</summary>
    public bool HasSfv { get; init; }

    /// <summary>True if all SFV-listed files are present and have correct CRC.</summary>
    public bool IsComplete { get; init; }

    /// <summary>True if the release is currently nuked.</summary>
    public bool IsNuked { get; init; }

    /// <summary>
    /// True if the release has ever been nuked (even if later unnuked).
    /// </summary>
    public bool WasNuked { get; init; }

    /// <summary>Who nuked the release (if known).</summary>
    public string? NukedBy { get; init; }

    /// <summary>Nuke reason (if any).</summary>
    public string? NukeReason { get; init; }

    /// <summary>Nuke multiplier (3.0x, 5.0x etc), if nuked.</summary>
    public double? NukeMultiplier { get; init; }

    /// <summary>When the release was nuked (if known).</summary>
    public DateTimeOffset? NukedAt { get; init; }

    /// <summary>When the first file in the release was seen.</summary>
    public DateTimeOffset Started { get; init; }

    /// <summary>When the release was last updated.</summary>
    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>Files in this release.</summary>
    public IReadOnlyList<ZipscriptFileInfo> Files { get; init; } = Array.Empty<ZipscriptFileInfo>();
}