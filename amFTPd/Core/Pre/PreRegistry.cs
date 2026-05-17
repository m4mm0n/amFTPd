using System.Collections.Concurrent;

namespace amFTPd.Core.Pre;

/// <summary>
/// Provides a thread-safe registry for storing and retrieving <see cref="PreEntry"/> instances by their virtual path.
/// </summary>
/// <remarks>
/// Live (Approved) pres are stored in <c>_pres</c> (keyed by VirtualPath).
/// Pending-approval pres are stored in <c>_pending</c> (keyed by ReleaseName, lower-cased).
/// All operations are safe for concurrent access.
/// </remarks>
public sealed class PreRegistry
{
    private readonly ConcurrentDictionary<string, PreEntry> _pres =
        new(StringComparer.OrdinalIgnoreCase);

    // Pending-approval queue: key = ReleaseName (lower-cased)
    private readonly ConcurrentDictionary<string, PreEntry> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // Live pre operations
    // ------------------------------------------------------------------

    public bool TryAdd(PreEntry entry)
        => _pres.TryAdd(entry.VirtualPath, entry);

    /// <summary>Adds or replaces an entry (e.g. on approval).</summary>
    public void AddOrReplace(PreEntry entry)
        => _pres[entry.VirtualPath] = entry;

    public bool Exists(string virtualPath)
        => _pres.ContainsKey(virtualPath);

    /// <summary>
    /// Finds a live pre by release name (case-insensitive).
    /// </summary>
    public PreEntry? FindByReleaseName(string releaseName)
        => _pres.Values.FirstOrDefault(e =>
            e.ReleaseName.Equals(releaseName, StringComparison.OrdinalIgnoreCase));

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

    public bool TryRemove(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
            return false;

        // Fast path: existing callers may still pass the virtual path as the dictionary key.
        if (_pres.TryRemove(releaseName, out _))
            return true;

        // Fallback path: remove by logical release name, matching behavior of PREINFO/DELPRE flows.
        return TryRemoveByRelease(releaseName);
    }

    public bool TryRemoveByRelease(string releaseName) =>
        (from kv in _pres
         where string.Equals(kv.Value.ReleaseName, releaseName, StringComparison.OrdinalIgnoreCase)
         select _pres.TryRemove(kv.Key, out _)).FirstOrDefault();

    // ------------------------------------------------------------------
    // Pending-approval queue operations
    // ------------------------------------------------------------------

    /// <summary>
    /// Enqueues a pre for staff approval.
    /// </summary>
    public bool EnqueuePending(PreEntry entry)
        => _pending.TryAdd(entry.ReleaseName.ToLowerInvariant(), entry);

    /// <summary>
    /// Returns all pres currently waiting for approval, newest first.
    /// </summary>
    public IReadOnlyList<PreEntry> PendingAll =>
        _pending.Values
            .OrderByDescending(p => p.Timestamp)
            .ToList();

    /// <summary>
    /// Attempts to find a pending pre by release name.
    /// </summary>
    public bool TryGetPending(string releaseName, out PreEntry? entry)
        => _pending.TryGetValue(releaseName.ToLowerInvariant(), out entry);

    /// <summary>
    /// Removes a pending entry from the queue (used after approve/deny).
    /// </summary>
    public bool RemovePending(string releaseName)
        => _pending.TryRemove(releaseName.ToLowerInvariant(), out _);

    public int PendingCount => _pending.Count;
}
