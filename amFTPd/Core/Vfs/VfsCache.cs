/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           VfsCache.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x98DEB06F
 *  
 *  Description:
 *      Represents a thread-safe, time-limited cache for storing and retrieving file system information.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







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
    private sealed record Entry(
        VfsResolveResult Result,
        DateTimeOffset ExpiresUtc);

    private readonly ConcurrentDictionary<string, Entry> _map = new();
    private readonly TimeSpan _ttl;

    public VfsCache(TimeSpan ttl) => _ttl = ttl;

    public bool TryGet(
        string key,
        out VfsResolveResult result)
    {
        result = default!;

        if (!_map.TryGetValue(key, out var entry))
            return false;

        if (DateTimeOffset.UtcNow > entry.ExpiresUtc)
        {
            _map.TryRemove(key, out _);
            return false;
        }

        result = entry.Result;
        return true;
    }

    public void Set(
        string key,
        VfsResolveResult result)
    {
        var expires = DateTimeOffset.UtcNow.Add(_ttl);
        _map[key] = new Entry(result, expires);
    }

    public void Clear()
        => _map.Clear();
}