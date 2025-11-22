using System.Collections.Concurrent;

namespace amFTPd.Core.Vfs;

/// <summary>
/// Represents a thread-safe, time-limited cache for storing and retrieving file system information.
/// </summary>
/// <remarks>The <see cref="VfsCache"/> class provides a mechanism to cache instances of <see
/// cref="FileSystemInfo"/> with a specified time-to-live (TTL). Cached entries automatically expire after the TTL
/// has elapsed, and expired entries are removed upon access.</remarks>
internal sealed class VfsCache
{
    private sealed record Entry(FileSystemInfo Info, DateTimeOffset ExpiresUtc);

    private readonly ConcurrentDictionary<string, Entry> _map = new();
    private readonly TimeSpan _ttl;

    public VfsCache(TimeSpan ttl) => _ttl = ttl;

    public bool TryGet(string key, out FileSystemInfo? info)
    {
        info = null;
        if (!_map.TryGetValue(key, out var entry))
            return false;

        if (DateTimeOffset.UtcNow > entry.ExpiresUtc)
        {
            _map.TryRemove(key, out _);
            return false;
        }

        info = entry.Info;
        return true;
    }

    public void Set(string key, FileSystemInfo info)
    {
        var expires = DateTimeOffset.UtcNow.Add(_ttl);
        _map[key] = new Entry(info, expires);
    }
}