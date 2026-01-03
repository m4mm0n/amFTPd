using System.Collections.Concurrent;

namespace amFTPd.Core.Pre;

/// <summary>
/// Provides a thread-safe registry for storing and retrieving <see cref="PreEntry"/> instances by their virtual path.
/// </summary>
/// <remarks>The registry uses case-insensitive comparison for virtual paths. All operations are safe for
/// concurrent access from multiple threads.</remarks>
public sealed class PreRegistry
{
    private readonly ConcurrentDictionary<string, PreEntry> _pres =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryAdd(PreEntry entry)
        => _pres.TryAdd(entry.VirtualPath, entry);

    public bool Exists(string virtualPath)
        => _pres.ContainsKey(virtualPath);

    public IReadOnlyList<PreEntry> All =>
        _pres.Values
            .OrderByDescending(p => p.Timestamp)
            .ToList();

    public IReadOnlyList<PreEntry> GetRecent(int max)
        => _pres.Values
            .OrderByDescending(p => p.Timestamp)
            .Take(max)
            .ToList();

    public IEnumerable<string> GetGroups()
        => _pres.Values
            .Select(e => e.Section)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<PreEntry> GetByGroup(string group)
        => _pres.Values
            .Where(e => e.Section.Equals(group, StringComparison.OrdinalIgnoreCase));

    public int CleanupExpired(DateTimeOffset now, TimeSpan ttl) => _pres.Where(kv => now - kv.Value.Timestamp > ttl)
        .Count(kv => _pres.TryRemove(kv.Key, out _));

    public bool TryRemove(string releaseName) => _pres.TryRemove(releaseName, out _);

    public bool TryRemoveByRelease(string releaseName) =>
        (from kv in _pres
            where string.Equals(kv.Value.ReleaseName, releaseName, StringComparison.OrdinalIgnoreCase)
            select _pres.TryRemove(kv.Key, out _)).FirstOrDefault();
}