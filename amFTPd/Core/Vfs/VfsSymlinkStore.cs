/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           VfsSymlinkStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24 00:00:00
 *  Last Modified:  2026-04-24 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Thread-safe, JSON-persisted registry of virtual-path symlinks.
 *      Maps a "link" virtual path to a "target" virtual path within the VFS.
 *      Used by SymlinkVfsProvider to resolve symlinks transparently.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using System.Text.Json;

namespace amFTPd.Core.Vfs;

/// <summary>
/// Represents a single VFS symlink entry (link path → target path).
/// </summary>
/// <param name="LinkPath">The virtual path of the symlink (e.g. "/INCOMING/GAMES").</param>
/// <param name="TargetPath">The virtual path the symlink points to (e.g. "/SECTIONS/GAMES").</param>
/// <param name="CreatedBy">User name of the siteop who created the link.</param>
/// <param name="CreatedAtUtc">UTC timestamp of creation.</param>
public sealed record VfsSymlink(
    string LinkPath,
    string TargetPath,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Thread-safe, JSON-persisted store of VFS virtual symlinks.
/// Maps a link virtual path to a target virtual path.
/// </summary>
public sealed class VfsSymlinkStore
{
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private Dictionary<string, VfsSymlink> _links; // key = normalised LinkPath

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public VfsSymlinkStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _links = Load(filePath);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the target virtual path for a given link path, or null if not found.
    /// </summary>
    public string? Resolve(string linkPath)
    {
        var key = Normalize(linkPath);
        lock (_lock)
            return _links.TryGetValue(key, out var entry) ? entry.TargetPath : null;
    }

    /// <summary>
    /// Returns all registered symlinks (snapshot).
    /// </summary>
    public IReadOnlyList<VfsSymlink> GetAll()
    {
        lock (_lock)
            return _links.Values.OrderBy(l => l.LinkPath).ToList();
    }

    /// <summary>
    /// Adds or replaces a symlink. Persists immediately.
    /// </summary>
    /// <returns>True if added, false if replaced.</returns>
    public bool AddOrReplace(string linkPath, string targetPath, string createdBy)
    {
        var key = Normalize(linkPath);
        bool added;
        lock (_lock)
        {
            added = !_links.ContainsKey(key);
            _links[key] = new VfsSymlink(
                LinkPath: key,
                TargetPath: Normalize(targetPath),
                CreatedBy: createdBy,
                CreatedAtUtc: DateTimeOffset.UtcNow);
            Save();
        }
        return added;
    }

    /// <summary>
    /// Removes a symlink by link path. Persists immediately.
    /// </summary>
    /// <returns>True if removed, false if it didn't exist.</returns>
    public bool Remove(string linkPath)
    {
        var key = Normalize(linkPath);
        bool removed;
        lock (_lock)
        {
            removed = _links.Remove(key);
            if (removed) Save();
        }
        return removed;
    }

    /// <summary>
    /// Returns true if a symlink exists for the given virtual path.
    /// </summary>
    public bool Contains(string linkPath)
    {
        var key = Normalize(linkPath);
        lock (_lock)
            return _links.ContainsKey(key);
    }

    // ------------------------------------------------------------------
    // Persistence
    // ------------------------------------------------------------------

    private void Save()
    {
        // caller holds _lock
        try
        {
            var list = _links.Values.ToList();
            var json = JsonSerializer.Serialize(list, _jsonOpts);
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // best-effort persistence; memory state remains authoritative
        }
    }

    private static Dictionary<string, VfsSymlink> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return new Dictionary<string, VfsSymlink>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(filePath);
            var list = JsonSerializer.Deserialize<List<VfsSymlink>>(json, _jsonOpts);
            if (list is null)
                return new Dictionary<string, VfsSymlink>(StringComparer.OrdinalIgnoreCase);

            return list.ToDictionary(
                l => Normalize(l.LinkPath),
                l => l with { LinkPath = Normalize(l.LinkPath), TargetPath = Normalize(l.TargetPath) },
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, VfsSymlink>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string Normalize(string path)
    {
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');
        return path;
    }
}
