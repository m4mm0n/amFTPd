using System.Text.Json;

namespace amFTPd.Config.Ftpd;

/// <summary>
/// Manages a collection of FTP sections, providing functionality to load, save, and retrieve sections based on virtual
/// paths.
/// </summary>
/// <remarks>The <see cref="SectionManager"/> class is designed to handle the configuration of FTP sections,
/// including loading from and saving to a file. It supports retrieving sections based on the longest prefix match of a
/// virtual path, ensuring that the most specific section is returned. If no match is found, a default section is used
/// as a fallback.</remarks>
public sealed class SectionManager
{
    private readonly List<FtpSection> _sections;
    private readonly string _path;

    private SectionManager(List<FtpSection> sections, string path)
    {
        _sections = sections;
        _path = path;
    }

    /// <summary>
    /// Loads a <see cref="SectionManager"/> instance from the specified configuration file. If the file does not exist,
    /// a default configuration is created, saved, and returned.
    /// </summary>
    /// <remarks>If the specified file does not exist, a default configuration is created with one free-leech
    /// section and saved to the specified path. The default section has the name "DEFAULT", a virtual root of "/", and
    /// a 1:1 upload-to-download ratio.</remarks>
    /// <param name="path">The path to the configuration file. Must be a valid file path.</param>
    /// <returns>A <see cref="SectionManager"/> instance initialized with the configuration data from the file, or a default
    /// configuration if the file does not exist.</returns>
    public static SectionManager LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            // Default: one free-leech section for everything
            var def = new FtpSection(
                Name: "DEFAULT",
                VirtualRoot: "/",
                FreeLeech: true,
                RatioUploadUnit: 1,
                RatioDownloadUnit: 1
            );

            var mgr = new SectionManager(new List<FtpSection> { def }, path);
            mgr.Save();
            return mgr;
        }

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<FtpSectionConfig>(json) ?? FtpSectionConfig.Empty;

        return new SectionManager(cfg.Sections, path);
    }
    /// <summary>
    /// Saves the current configuration to a file in JSON format.
    /// </summary>
    /// <remarks>The configuration is serialized with indented formatting and written to the file specified by
    /// the internal path.</remarks>
    public void Save()
    {
        var cfg = new FtpSectionConfig(_sections);
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }
    /// <summary>
    /// Retrieves a read-only list of FTP sections.
    /// </summary>
    /// <returns>A read-only list of <see cref="FtpSection"/> objects representing the FTP sections.</returns>
    public IReadOnlyList<FtpSection> GetSections() => _sections.AsReadOnly();
    /// <summary>
    /// Retrieves the FTP section that corresponds to the specified virtual path.
    /// </summary>
    /// <remarks>The method normalizes the virtual path to use forward slashes ('/') and ensures it starts
    /// with a leading slash. It performs a case-insensitive comparison to find the section with the longest matching
    /// virtual root.</remarks>
    /// <param name="virtualPath">The virtual path for which to find the corresponding FTP section. The path can use either forward slashes ('/')
    /// or backslashes ('\') as directory separators.</param>
    /// <returns>The <see cref="FtpSection"/> that best matches the specified virtual path based on the longest-prefix match. If
    /// no match is found, the first section in the collection is returned as a fallback.</returns>
    public FtpSection GetSectionForPath(string virtualPath)
    {
        // Normalize to slash
        var vp = virtualPath.Replace('\\', '/');
        if (!vp.StartsWith('/'))
            vp = "/" + vp;

        // Longest-prefix match
        var best = _sections
            .Where(s => vp.StartsWith(s.VirtualRoot, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.VirtualRoot.Length)
            .FirstOrDefault();

        return best ?? _sections[0]; // fallback to first
    }
}