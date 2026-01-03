using System.Text.Json;

namespace amFTPd.Core.Pre;

/// <summary>
/// Provides methods to persist and restore the state of a <see cref="PreRegistry"/> to and from a file.
/// </summary>
/// <remarks>This class is intended for use with <see cref="PreRegistry"/> instances that need to be saved to disk
/// and later reloaded. It is not thread-safe; callers should ensure appropriate synchronization if accessing from
/// multiple threads.</remarks>
public sealed class PreRegistryPersistence
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new()
        {
            WriteIndented = true
        };

    /// <summary>
    /// Persist all PRE entries to disk using an atomic write.
    /// </summary>
    public void Save(PreRegistry registry, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";

        var json = JsonSerializer.Serialize(
            registry.All,
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Load PRE entries from disk into the registry.
    /// Existing entries are preserved unless overridden by path key.
    /// </summary>
    public void Load(PreRegistry registry, string path)
    {
        if (registry is null)
            throw new ArgumentNullException(nameof(registry));

        if (!File.Exists(path))
            return;

        var json = File.ReadAllText(path);

        var entries = JsonSerializer.Deserialize<List<PreEntry>>(json);
        if (entries is null)
            return;

        foreach (var entry in entries)
        {
            // Idempotent: registry key is VirtualPath
            registry.TryAdd(entry);
        }
    }
}