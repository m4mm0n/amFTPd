/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpSection.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 18:01:19
 *  CRC32:          0x838615BE
 *  
 *  Description:
 *      Logical FTP section (like "0DAY").
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


namespace amFTPd.Config.Ftpd;

/// <summary>
/// Logical FTP section (like "0DAY").
/// </summary>
public sealed record FtpSection
{
    /// <summary>Internal name of the section.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Compatibility alias (SectionName) used in other parts.</summary>
    public string SectionName
    {
        get => Name;
        init => Name = value;
    }

    public string Description { get; init; } = string.Empty;

    /// <summary>Virtual root path (e.g. "/0DAY").</summary>
    public string VirtualRoot { get; init; } = "/";

    /// <summary>
    /// Compatibility alias (VirtualPath) used by legacy configs/files.
    /// (Some older configs use "VirtualPath" instead of "VirtualRoot").
    /// </summary>
    public string VirtualPath
    {
        get => VirtualRoot;
        init => VirtualRoot = value;
    }

    /// <summary>Compatibility alias for virtual root.</summary>
    public string RelativePath
    {
        get => VirtualRoot;
        init => VirtualRoot = value;
    }

    /// <summary>Ratio section name (links to ratio rules).</summary>
    public string? RatioSection { get; init; }

    /// <summary>Compatibility alias used by older section JSON files.</summary>
    public string? RatioRuleName
    {
        get => RatioSection;
        init => RatioSection = value;
    }

    /// <summary>Allow uploads in this section.</summary>
    public bool AllowUpload { get; init; } = true;

    /// <summary>Allow downloads in this section.</summary>
    public bool AllowDownload { get; init; } = true;

    /// <summary>If true, this is free-leech (no ratio cost).</summary>
    public bool FreeLeech { get; init; }

    /// <summary>Upload unit (bytes or KiB) for ratio accounting.</summary>
    public long RatioUploadUnit { get; init; }

    /// <summary>Download unit (bytes or KiB) for ratio accounting.</summary>
    public long RatioDownloadUnit { get; init; }

    /// <summary>Upload multiplier for credits (e.g. 2x).</summary>
    public double UploadMultiplier { get; init; } = 1.0;

    /// <summary>Download multiplier for credit cost.</summary>
    public double DownloadMultiplier { get; init; } = 1.0;

    /// <summary>Nuke multiplier for nuking releases.</summary>
    public double? NukeMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Maximum upload speed for this section in KB/s. 0 = unlimited.
    /// Acts as a cap on top of the per-user MaxUploadKbps.
    /// </summary>
    public int MaxUploadKbps { get; init; }

    /// <summary>
    /// Maximum download speed for this section in KB/s. 0 = unlimited.
    /// Acts as a cap on top of the per-user MaxDownloadKbps.
    /// </summary>
    public int MaxDownloadKbps { get; init; }

    /// <summary>
    /// Optional aliases for this section name, used for compatibility with
    /// legacy configs (gl/io/raiden) and for convenience.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    /// <summary>
    /// If true, an .sfv file must be present in the release directory before
    /// any non-.sfv file may be uploaded. Rejects uploads with 550 until
    /// the SFV arrives first.
    /// </summary>
    public bool RequireSfvFirst { get; init; }

    /// <summary>
    /// If true, a REST-based resume (REST N + STOR) is verified before accepting:
    /// the partial file on disk must be exactly N bytes AND its CRC32 must match
    /// the stored partial-file checksum (if available). Rejects with 550 + re-upload
    /// instruction on mismatch. Defaults to false.
    /// </summary>
    public bool RequireResumeIntegrity { get; init; }

    /// <summary>
    /// The groups that are allowed to PRE releases into this section.
    /// Empty list = any group (or siteop) may PRE.
    /// If non-empty, only the listed groups (and siteops) may use SITE PRE for this section.
    /// </summary>
    public IReadOnlyList<string> AllowedPreGroups { get; init; } = Array.Empty<string>();

    /// <summary>
    /// If true, SITE PRE commands targeting this section go into a pending queue and
    /// must be approved by a siteop via SITE PREAPPROVE before going live.
    /// Defaults to false (immediate pre).
    /// </summary>
    public bool RequirePreApproval { get; init; }

    public FtpSection()
    {
    }

    /// <summary>
    /// Compatibility ctor – supports named arg "Name" (and others).
    /// </summary>
    public FtpSection(
        string Name,
        string VirtualRoot,
        string? RatioSection = null,
        bool AllowUpload = true,
        bool AllowDownload = true,
        bool FreeLeech = false,
        long RatioUploadUnit = 0,
        long RatioDownloadUnit = 0,
        double UploadMultiplier = 1.0,
        double DownloadMultiplier = 1.0,
        double NukeMultiplier = 1.0,
        string Description = "",
        int MaxUploadKbps = 0,
        int MaxDownloadKbps = 0,
        bool RequireSfvFirst = false,
        bool RequireResumeIntegrity = false,
        IReadOnlyList<string>? AllowedPreGroups = null,
        bool RequirePreApproval = false)
    {
        this.Name = Name;
        this.VirtualRoot = VirtualRoot;
        this.RatioSection = RatioSection;
        this.AllowUpload = AllowUpload;
        this.AllowDownload = AllowDownload;
        this.FreeLeech = FreeLeech;
        this.RatioUploadUnit = RatioUploadUnit;
        this.RatioDownloadUnit = RatioDownloadUnit;
        this.UploadMultiplier = UploadMultiplier;
        this.DownloadMultiplier = DownloadMultiplier;
        this.NukeMultiplier = NukeMultiplier;
        this.Description = Description;
        this.MaxUploadKbps = MaxUploadKbps;
        this.MaxDownloadKbps = MaxDownloadKbps;
        this.RequireSfvFirst = RequireSfvFirst;
        this.RequireResumeIntegrity = RequireResumeIntegrity;
        this.AllowedPreGroups = AllowedPreGroups ?? Array.Empty<string>();
        this.RequirePreApproval = RequirePreApproval;
    }
}

