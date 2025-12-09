/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IdentCache.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xEE95E903
 *  
 *  Description:
 *      Provides a thread-safe cache for storing and retrieving <see cref="IdentResult"/> values associated with <see cref="I...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Collections.Concurrent;
using System.Net;

namespace amFTPd.Core.Ident;

/// <summary>
/// Provides a thread-safe cache for storing and retrieving <see cref="IdentResult"/> values associated with <see
/// cref="IPAddress"/> keys, with support for automatic expiration of entries.
/// </summary>
/// <remarks>This cache is designed to store identification results for a configurable time-to-live (TTL)
/// period. Entries are automatically removed when they expire, ensuring that stale data is not returned. The cache
/// is thread-safe and can be accessed concurrently from multiple threads.</remarks>
internal sealed class IdentCache
{
    private sealed record Entry(IdentResult Result, DateTimeOffset ExpiresUtc);

    private readonly ConcurrentDictionary<IPAddress, Entry> _entries = new();
    private readonly TimeSpan _ttl;

    public IdentCache(TimeSpan ttl) => _ttl = ttl;

    public bool TryGet(IPAddress key, out IdentResult result)
    {
        result = IdentResult.Failed;
        if (!_entries.TryGetValue(key, out var entry))
            return false;

        if (DateTimeOffset.UtcNow > entry.ExpiresUtc)
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        result = entry.Result;
        return true;
    }

    public void Set(IPAddress key, IdentResult result)
    {
        var expires = DateTimeOffset.UtcNow.Add(_ttl);
        _entries[key] = new Entry(result, expires);
    }
}