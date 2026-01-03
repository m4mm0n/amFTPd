using System.Text.Json;

namespace amFTPd.Core.Scene;

/// <summary>
/// Provides methods for persisting and restoring the state of a scene registry to and from a file.
/// </summary>
/// <remarks>Use this class to save the current state of a scene registry to disk and to reload it later. The
/// persistence format is JSON, and the file location is specified by the caller. This class does not provide thread
/// safety; callers should ensure that concurrent access to the registry and file system is managed
/// appropriately.</remarks>
public sealed class SceneRegistryPersistence
{
    public void Save(SceneStateRegistry registry, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(
            registry.GetAll(),
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(path, json);
    }

    public void Load(SceneStateRegistry registry, string path)
    {
        if (!File.Exists(path))
            return;

        var json = File.ReadAllText(path);
        var states = JsonSerializer.Deserialize<List<SceneReleaseState>>(json);
        if (states is null)
            return;

        foreach (var s in states)
            registry.Restore(s);
    }
}
