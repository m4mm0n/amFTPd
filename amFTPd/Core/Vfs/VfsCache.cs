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

    /// <summary>
    /// Initializes a new instance of the <see cref="VfsCache"/> class with a specified time-to-live (TTL) duration.
    /// </summary>
    /// <param name="ttl">The time-to-live duration for cached items. Cached items will remain valid for the specified duration before
    /// being considered expired.</param>
    public VfsCache(TimeSpan ttl) => _ttl = ttl;
    /// <summary>
    /// Attempts to retrieve the <see cref="FileSystemInfo"/> associated with the specified key.
    /// </summary>
    /// <remarks>If the key exists but the associated entry has expired, the entry is removed from the
    /// internal map, and the method returns <see langword="false"/>.</remarks>
    /// <param name="key">The key used to locate the associated <see cref="FileSystemInfo"/>.</param>
    /// <param name="info">When this method returns, contains the <see cref="FileSystemInfo"/> associated with the specified key, if the
    /// key exists and has not expired; otherwise, <see langword="null"/>. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the key exists and the associated <see cref="FileSystemInfo"/> has not expired;
    /// otherwise, <see langword="false"/>.</returns>
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
    /// <summary>
    /// Adds or updates an entry in the collection with the specified key and associated file system information.
    /// </summary>
    /// <remarks>If an entry with the specified key already exists, it will be updated with the new file
    /// system information and expiration time. The expiration time is calculated based on the current time and the
    /// configured time-to-live (TTL) value.</remarks>
    /// <param name="key">The unique identifier for the entry. Cannot be <see langword="null"/> or empty.</param>
    /// <param name="info">The file system information to associate with the key. Cannot be <see langword="null"/>.</param>
    public void Set(string key, FileSystemInfo info)
    {
        var expires = DateTimeOffset.UtcNow.Add(_ttl);
        _map[key] = new Entry(info, expires);
    }
}