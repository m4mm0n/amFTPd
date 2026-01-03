using System.Text.Json;

namespace amFTPd.Core.ReleaseSystem;

/// <summary>
/// Persists and restores the in-memory <see cref="ReleaseRegistry"/> to/from disk.
/// </summary>
/// <remarks>
/// The persistence format is JSON and is intended as a lightweight recovery mechanism so the daemon can
/// retain recently-seen release state across restarts. Callers are responsible for choosing an appropriate
/// file location (typically next to the main config file).
/// </remarks>
public sealed class ReleaseRegistryPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private sealed class Snapshot
    {
        public string Section { get; set; } = string.Empty;
        public string ReleaseName { get; set; } = string.Empty;
        public ReleaseState State { get; set; } = ReleaseState.New;
        public DateTimeOffset FirstSeen { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public bool HasSfv { get; set; }
        public bool HasNfo { get; set; }
        public bool HasDiz { get; set; }
    }

    public void Save(ReleaseRegistry registry, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var snapshots = registry.All
            .Select(r => new Snapshot
            {
                Section = r.Section,
                ReleaseName = r.ReleaseName,
                State = r.State,
                FirstSeen = r.FirstSeen,
                LastUpdated = r.LastUpdated,
                FileCount = r.FileCount,
                TotalBytes = r.TotalBytes,
                HasSfv = r.HasSfv,
                HasNfo = r.HasNfo,
                HasDiz = r.HasDiz
            })
            .ToList();

        var json = JsonSerializer.Serialize(
            snapshots,
            JsonOptions);

        AtomicWriteAllText(path, json);
    }

    public void Load(ReleaseRegistry registry, string path)
    {
        if (!File.Exists(path))
            return;

        var json = File.ReadAllText(path);
        var snapshots = JsonSerializer.Deserialize<List<Snapshot>>(json);
        if (snapshots is null)
            return;

        foreach (var s in snapshots)
        {
            if (string.IsNullOrWhiteSpace(s.Section) || string.IsNullOrWhiteSpace(s.ReleaseName))
                continue;

            var ctx = registry.GetOrCreate(s.Section, s.ReleaseName);

            ctx.State = s.State;
            ctx.FirstSeen = s.FirstSeen;
            ctx.LastUpdated = s.LastUpdated;
            ctx.FileCount = s.FileCount;
            ctx.TotalBytes = s.TotalBytes;
            ctx.HasSfv = s.HasSfv;
            ctx.HasNfo = s.HasNfo;
            ctx.HasDiz = s.HasDiz;
        }
    }

    private static void AtomicWriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        var bak = path + ".bak";

        // Write to a temporary file first.
        File.WriteAllText(tmp, contents);

        // Best-effort backup of the existing file.
        try
        {
            if (File.Exists(path))
                File.Copy(path, bak, overwrite: true);
        }
        catch
        {
            // ignore backup failures
        }

        // Replace atomically when possible.
        try
        {
            if (OperatingSystem.IsWindows() && File.Exists(path))
            {
                File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path, overwrite: true);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch { }
        }
    }
}
