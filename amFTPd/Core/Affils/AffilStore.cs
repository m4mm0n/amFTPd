/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AffilStore.cs
 *  Created:        2026-04-24
 *  Description:    Thread-safe affil store (section → set of affiliated groups) with JSON persistence.
 *                  Persists to {configDir}/affils.json.
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text.Json;

namespace amFTPd.Core.Affils;

/// <summary>
/// Stores which groups are affiliated with which sections.
/// Keyed by section name (case-insensitive) → sorted set of group names (case-insensitive).
/// </summary>
public sealed class AffilStore
{
    private readonly string _filePath;
    private readonly Lock _lock = new();

    // section → groups
    private Dictionary<string, SortedSet<string>> _affils =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AffilStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        Load();
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Affiliate <paramref name="group"/> with <paramref name="section"/>. Returns false if already affiliated.</summary>
    public bool Add(string section, string group)
    {
        bool added;
        lock (_lock)
        {
            if (!_affils.TryGetValue(section, out var set))
            {
                set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                _affils[section] = set;
            }
            added = set.Add(group);
        }
        if (added) Save();
        return added;
    }

    /// <summary>Remove <paramref name="group"/> from <paramref name="section"/>. Returns false if not found.</summary>
    public bool Remove(string section, string group)
    {
        bool removed;
        lock (_lock)
        {
            if (!_affils.TryGetValue(section, out var set))
                return false;
            removed = set.Remove(group);
            if (set.Count == 0)
                _affils.Remove(section);
        }
        if (removed) Save();
        return removed;
    }

    /// <summary>Return all affils, optionally filtered to a single section.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetAll(string? sectionFilter = null)
    {
        lock (_lock)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sec, groups) in _affils)
            {
                if (sectionFilter is not null &&
                    !sec.Equals(sectionFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                result[sec] = groups.ToList();
            }
            return result;
        }
    }

    /// <summary>Return the groups affiliated with a specific section.</summary>
    public IReadOnlyList<string> GetForSection(string section)
    {
        lock (_lock)
        {
            if (_affils.TryGetValue(section, out var set))
                return set.ToList();
            return [];
        }
    }

    /// <summary>True if <paramref name="group"/> is affiliated with <paramref name="section"/>.</summary>
    public bool IsAffiliated(string section, string group)
    {
        lock (_lock)
            return _affils.TryGetValue(section, out var set) && set.Contains(group);
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
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOpts);
            if (raw is not null)
            {
                lock (_lock)
                {
                    _affils = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (sec, groups) in raw)
                        _affils[sec] = new SortedSet<string>(groups, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            Dictionary<string, List<string>> raw;
            lock (_lock)
                raw = _affils.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase);

            File.WriteAllText(_filePath, JsonSerializer.Serialize(raw, JsonOpts));
        }
        catch { }
    }
}
