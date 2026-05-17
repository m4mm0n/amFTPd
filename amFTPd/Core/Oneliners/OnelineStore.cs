/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           OnelineStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24
 *
 *  Description:
 *      Thread-safe in-memory oneliner store with JSON persistence.
 *      Persists to {configDir}/oneliners.json on every mutation.
 *      Capacity-bounded: oldest entries are dropped when MaxLines is exceeded.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text.Json;

namespace amFTPd.Core.Oneliners;

/// <summary>
/// Thread-safe in-memory oneliner (shoutbox) store with JSON persistence.
/// </summary>
public sealed class OnelineStore
{
    private readonly string _filePath;
    private readonly int _maxLines;
    private readonly Lock _lock = new();

    // Stored newest-first for cheap head-slicing.
    private List<OnelineEntry> _lines = [];
    private int _nextId = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public OnelineStore(string filePath, int maxLines = 200)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _maxLines = maxLines > 0 ? maxLines : 200;
        Load();
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Add a new oneliner. Returns the created entry.</summary>
    public OnelineEntry Add(string userName, string? groupName, string message)
    {
        var entry = new OnelineEntry
        {
            Id = 0, // assigned below inside lock
            UserName = userName,
            GroupName = groupName,
            Message = message,
            PostedAt = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            entry = entry with { Id = _nextId++ };
            _lines.Insert(0, entry); // newest first

            if (_lines.Count > _maxLines)
                _lines.RemoveRange(_maxLines, _lines.Count - _maxLines);
        }

        Save();
        return entry;
    }

    /// <summary>Return the last <paramref name="count"/> entries, newest first.</summary>
    public IReadOnlyList<OnelineEntry> GetRecent(int count = 10)
    {
        lock (_lock)
            return _lines.Take(count > 0 ? count : 10).ToList();
    }

    /// <summary>Delete a oneliner by ID. Returns false if not found.</summary>
    public bool Delete(int id)
    {
        bool removed;
        lock (_lock)
            removed = _lines.RemoveAll(e => e.Id == id) > 0;

        if (removed)
            Save();

        return removed;
    }

    /// <summary>Wipe all oneliners.</summary>
    public void WipeAll()
    {
        lock (_lock)
            _lines.Clear();

        Save();
    }

    /// <summary>Total number of stored lines.</summary>
    public int Count
    {
        get { lock (_lock) return _lines.Count; }
    }

    // ------------------------------------------------------------------
    // Persistence
    // ------------------------------------------------------------------

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<OnelineEntry>>(json, JsonOpts);
            if (loaded is not null && loaded.Count > 0)
            {
                lock (_lock)
                {
                    _lines = loaded.OrderByDescending(e => e.PostedAt).ToList();
                    _nextId = _lines.Max(e => e.Id) + 1;
                }
            }
        }
        catch
        {
            // Corrupt file — start fresh.
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            List<OnelineEntry> snapshot;
            lock (_lock)
                snapshot = [.. _lines];

            File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, JsonOpts));
        }
        catch
        {
            // Best-effort — never crash the session on persistence failure.
        }
    }
}
