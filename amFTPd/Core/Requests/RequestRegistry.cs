/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RequestRegistry.cs
 *  Created:        2026-04-24
 *  Description:    Thread-safe request registry with JSON persistence.
 *                  Requests expire after a configurable TTL (default 30 days).
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text.Json;

namespace amFTPd.Core.Requests;

/// <summary>
/// Thread-safe release request registry with JSON persistence.
/// Requests can be pending or filled. Filled/expired entries are purged by the housekeeping loop.
/// </summary>
public sealed class RequestRegistry
{
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private List<RequestEntry> _requests = [];
    private int _nextId = 1;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RequestRegistry(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        Load();
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Add a new request. Returns null if a request for this release already exists (open).</summary>
    public RequestEntry? Add(string releaseName, string requestedBy, string? groupName)
    {
        lock (_lock)
        {
            if (_requests.Any(r => !r.IsFilled &&
                r.ReleaseName.Equals(releaseName, StringComparison.OrdinalIgnoreCase)))
                return null;

            var entry = new RequestEntry
            {
                Id = _nextId++,
                ReleaseName = releaseName,
                RequestedBy = requestedBy,
                GroupName = groupName,
                RequestedAt = DateTimeOffset.UtcNow
            };
            _requests.Insert(0, entry);
            Save();
            return entry;
        }
    }

    /// <summary>Mark a request as filled. Returns null if not found or already filled.</summary>
    public RequestEntry? MarkFilled(string releaseName, string filledBy)
    {
        lock (_lock)
        {
            var idx = _requests.FindIndex(r => !r.IsFilled &&
                r.ReleaseName.Equals(releaseName, StringComparison.OrdinalIgnoreCase));

            if (idx < 0) return null;

            var updated = _requests[idx] with
            {
                IsFilled = true,
                FilledBy = filledBy,
                FilledAt = DateTimeOffset.UtcNow
            };
            _requests[idx] = updated;
            Save();
            return updated;
        }
    }

    /// <summary>Delete a request by ID. Returns false if not found.</summary>
    public bool Delete(int id)
    {
        bool removed;
        lock (_lock)
            removed = _requests.RemoveAll(r => r.Id == id) > 0;

        if (removed) Save();
        return removed;
    }

    /// <summary>Wipe all requests (open and filled).</summary>
    public void WipeAll()
    {
        lock (_lock)
            _requests.Clear();
        Save();
    }

    /// <summary>Return open requests, optionally filtered by substring match on release name.</summary>
    public IReadOnlyList<RequestEntry> GetOpen(string? filter = null)
    {
        lock (_lock)
        {
            var q = _requests.Where(r => !r.IsFilled);
            if (!string.IsNullOrWhiteSpace(filter))
                q = q.Where(r => r.ReleaseName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            return q.OrderByDescending(r => r.RequestedAt).ToList();
        }
    }

    /// <summary>Purge filled or expired requests. Returns number removed.</summary>
    public int CleanupExpired(DateTimeOffset now, TimeSpan ttl)
    {
        int removed;
        lock (_lock)
            removed = _requests.RemoveAll(r =>
                r.IsFilled || now - r.RequestedAt > ttl);

        if (removed > 0) Save();
        return removed;
    }

    // ------------------------------------------------------------------
    // Persistence
    // ------------------------------------------------------------------

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<RequestEntry>>(json, JsonOpts);
            if (loaded is { Count: > 0 })
            {
                lock (_lock)
                {
                    _requests = loaded;
                    _nextId = _requests.Max(e => e.Id) + 1;
                }
            }
        }
        catch { /* corrupt — start fresh */ }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            List<RequestEntry> snapshot;
            lock (_lock)
                snapshot = [.. _requests];

            File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, JsonOpts));
        }
        catch { }
    }
}
