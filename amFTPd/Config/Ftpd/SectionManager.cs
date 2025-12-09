/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SectionManager.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xC5AB776C
 *  
 *  Description:
 *      Holds the runtime collection of <see cref="FtpSection"/> objects and provides helpers for loading them from JSON / DB.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using amFTPd.Db;
using System.Text.Json;

namespace amFTPd.Config.Ftpd;

/// <summary>
/// Holds the runtime collection of <see cref="FtpSection"/> objects and
/// provides helpers for loading them from JSON / DB.
/// </summary>
public sealed class SectionManager
{
    private readonly IReadOnlyList<FtpSection> _sections;

    public SectionManager(IEnumerable<FtpSection> sections)
    {
        _sections = (sections ?? Array.Empty<FtpSection>())
            .Select(s => s.Normalize())
            .OrderByDescending(s => s.VirtualRoot.Length)
            .ToArray();
    }

    /// <summary>
    /// Compatibility constructor – keeps older call sites that pass a
    /// source description (e.g. "in-memory", "db") compiling.
    /// </summary>
    public SectionManager(IEnumerable<FtpSection> sections, string sourceDescription)
        : this(sections)
    {
        // sourceDescription is currently informational only.
        // You can log or store it later if needed.
    }

    /// <summary>
    /// All sections known at runtime.
    /// </summary>
    public IReadOnlyList<FtpSection> GetSections() => _sections;

    /// <summary>
    /// Returns the best matching <see cref="FtpSection"/> for a given virtual path.
    /// Uses longest-prefix match on the section's VirtualRoot.
    /// Never returns null – if nothing matches, a fallback section is returned.
    /// </summary>
    public FtpSection GetSectionForPath(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            virtualPath = "/";

        virtualPath = virtualPath.Replace('\\', '/');
        if (!virtualPath.StartsWith("/"))
            virtualPath = "/" + virtualPath;

        // try to find a matching section by prefix
        var match = _sections.FirstOrDefault(sec =>
            virtualPath.StartsWith(sec.VirtualRoot, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        // Fallback: if we have at least one section, return the first one.
        if (_sections.Count > 0)
            return _sections[0];

        // Ultimate fallback: synthetic default section
        return new FtpSection(
            Name: "DEFAULT",
            VirtualRoot: "/",
            FreeLeech: false,
            RatioUploadUnit: 1,
            RatioDownloadUnit: 1,
            UploadMultiplier: 1.0,
            DownloadMultiplier: 1.0,
            NukeMultiplier: 1
        ).Normalize();
    }


    // ---------------------------------------------------------------------
    // JSON FILE BACKEND
    // ---------------------------------------------------------------------

    /// <summary>
    /// Loads sections from a JSON file at <paramref name="path"/>.
    /// The file is expected to contain a JSON array of FtpSection objects.
    /// </summary>
    public static SectionManager LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentNullException(nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException("Sections file not found.", path);

        var json = File.ReadAllText(path);
        var sections = JsonSerializer.Deserialize<List<FtpSection>>(json)
                       ?? new List<FtpSection>();

        return new SectionManager(sections);
    }

    /// <summary>
    /// Loads sections from <paramref name="path"/>, creating a minimal
    /// default file if it does not exist yet.
    /// </summary>
    public static SectionManager LoadOrCreateDefault(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentNullException(nameof(path));

        if (!File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var defaultSections = new List<FtpSection>
                {
                    new FtpSection(
                        Name: "DEFAULT",
                        VirtualRoot: "/",
                        FreeLeech: false,
                        RatioUploadUnit: 1,
                        RatioDownloadUnit: 1,
                        UploadMultiplier: 1.0,
                        DownloadMultiplier: 1.0,
                        NukeMultiplier: 1
                    ).Normalize()
                };

            var json = JsonSerializer.Serialize(
                defaultSections,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(path, json);
            return new SectionManager(defaultSections);
        }

        return LoadFromFile(path);
    }

    // ---------------------------------------------------------------------
    // DB BACKEND (ISectionStore) – stubbed for now
    // ---------------------------------------------------------------------

    /// <summary>
    /// Creates a SectionManager from a section-store backend.
    /// Currently returns an empty set; hook into the real ISectionStore
    /// implementation whenever you’re ready.
    /// </summary>
    public static SectionManager FromSectionStore(ISectionStore store, string sourceDescription)
    {
        if (store is null)
            throw new ArgumentNullException(nameof(store));
        if (string.IsNullOrWhiteSpace(sourceDescription))
            throw new ArgumentException("Source description must be provided.", nameof(sourceDescription));

        // Pull all sections from the store.
        // BinarySectionStore / InMemorySectionStore already store actual Config.Ftpd.FtpSection
        // instances, so we don’t need to map – we can pass them straight into SectionManager.
        var allSections = store
            .GetAllSections()
            .Where(s => s is not null)
            .ToList();

        if (allSections.Count == 0)
        {
            // No sections in DB – this is *probably* a configuration problem,
            // so fail loudly rather than silently running with no sections.
            throw new InvalidOperationException(
                $"Section store '{sourceDescription}' returned no sections. " +
                "Check your sections DB / configuration.");
        }

        return new SectionManager(allSections);
    }
}