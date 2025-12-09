/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpSectionConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x34E5DA79
 *  
 *  Description:
 *      Aggregates all configured sections and provides helpers for lookups.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Config.Ftpd;

/// <summary>
/// Aggregates all configured sections and provides helpers for lookups.
/// </summary>
public sealed record FtpSectionConfig
{
    public static FtpSectionConfig Empty { get; } = new();

    public List<FtpSection> Sections { get; init; } = new();

    // (Optional) convenience view
    public IReadOnlyList<FtpSection> SectionsReadonly => Sections;

    public string? DefaultSectionName { get; init; }

    public FtpSectionConfig()
    {
    }

    public FtpSectionConfig(IReadOnlyList<FtpSection> sections)
    {
        Sections = sections.ToList();
    }

    public FtpSection? GetSection(string name)
        => Sections.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public FtpSection? ResolveByPath(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            virtualPath = "/";

        if (!virtualPath.StartsWith('/'))
            virtualPath = "/" + virtualPath;

        var winner = Sections
            .OrderByDescending(s => s.VirtualRoot.Length)
            .FirstOrDefault(s =>
            {
                var root = s.VirtualRoot;
                if (!root.StartsWith('/'))
                    root = "/" + root;
                return virtualPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            });

        if (winner is not null)
            return winner;

        if (!string.IsNullOrWhiteSpace(DefaultSectionName))
            return GetSection(DefaultSectionName);

        return null;
    }
}