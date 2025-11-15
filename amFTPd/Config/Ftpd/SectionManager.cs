using System.Text.Json;

namespace amFTPd.Config.Ftpd;

public sealed class SectionManager
{
    private readonly List<FtpSection> _sections;
    private readonly string _path;

    private SectionManager(List<FtpSection> sections, string path)
    {
        _sections = sections;
        _path = path;
    }

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

    public void Save()
    {
        var cfg = new FtpSectionConfig(_sections);
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    public IReadOnlyList<FtpSection> GetSections() => _sections.AsReadOnly();

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