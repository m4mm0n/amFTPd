/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FileDupeStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 04:35:27
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x35390D07
 *  
 *  Description:
 *      File-backed dupe store. Keeps everything in memory and saves to a JSON file on each change. Good enough for typical s...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Text.Json;
using System.Text.RegularExpressions;

namespace amFTPd.Core.Dupe;

/// <summary>
/// File-backed dupe store. Keeps everything in memory and saves to a JSON file on each change.
/// Good enough for typical site scales; can be upgraded later if needed.
/// </summary>
public sealed class FileDupeStore : IDupeStore, IDisposable
{
    private readonly string _filePath;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<string, DupeEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public FileDupeStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        LoadFromDisk();
    }

    public DupeEntry? Find(string sectionName, string releaseName)
    {
        var key = DupeEntry.MakeKey(sectionName, releaseName);

        _lock.EnterReadLock();
        try
        {
            return _entries.TryGetValue(key, out var entry) ? entry : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<DupeEntry> Search(string pattern, string? sectionName = null, int limit = 50)
    {
        pattern ??= "*";

        var regex = WildcardToRegex(pattern);
        var sectionFilter = sectionName?.Trim();
        var list = new List<DupeEntry>(Math.Min(limit, 128));

        _lock.EnterReadLock();
        try
        {
            foreach (var entry in _entries.Values)
            {
                if (sectionFilter is not null &&
                    !entry.SectionName.Equals(sectionFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!regex.IsMatch(entry.ReleaseName))
                    continue;

                list.Add(entry);
                if (list.Count >= limit)
                    break;
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return list;
    }

    public void Upsert(DupeEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        var key = entry.Key;
        _lock.EnterWriteLock();
        try
        {
            _entries[key] = entry with
            {
                // ensure LastUpdated is always set
                LastUpdated = entry.LastUpdated == default
                    ? DateTimeOffset.UtcNow
                    : entry.LastUpdated,
                FirstSeen = entry.FirstSeen == default
                    ? DateTimeOffset.UtcNow
                    : entry.FirstSeen
            };
            SaveToDiskNoLock();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool Remove(string sectionName, string releaseName)
    {
        var key = DupeEntry.MakeKey(sectionName, releaseName);

        _lock.EnterWriteLock();
        try
        {
            var removed = _entries.Remove(key);
            if (removed)
            {
                SaveToDiskNoLock();
            }
            return removed;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<List<DupeEntry>>(json) ?? new List<DupeEntry>();

            foreach (var e in entries)
            {
                var key = e.Key;
                _entries[key] = e;
            }
        }
        catch
        {
            // If the dupe file is corrupt, we start empty rather than crash the daemon.
            // You can log this if you want.
            _entries.Clear();
        }
    }

    private void SaveToDiskNoLock()
    {
        var snapshot = _entries.Values.ToList();
        var json = JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions { WriteIndented = false });

        File.WriteAllText(_filePath, json);
    }

    private static Regex WildcardToRegex(string pattern)
    {
        // Simple wildcard (* and ?) to regex, case-insensitive.
        var escaped = Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}