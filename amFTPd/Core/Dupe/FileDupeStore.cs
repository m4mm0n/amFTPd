/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FileDupeStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 04:35:27
 *  Last Modified:  2025-12-14 01:10:52
 *  CRC32:          0x0AA4F3C0
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
 * ====================================================================================================
 */


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

    private const int SaveBatchThreshold = 32;
    private int _pendingSaves;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDupeStore"/> class using the specified file path for persistent
    /// storage.
    /// </summary>
    /// <remarks>If the directory for the specified file path does not exist, it is created automatically. The
    /// constructor loads any existing data from the file into memory.</remarks>
    /// <param name="filePath">The full path to the file used for storing duplicate information. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="filePath"/> is null.</exception>
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

        List<DupeEntry>? snapshot = null;
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

            if (ShouldPersist())
            {
                snapshot = _entries.Values.ToList();
                _pendingSaves = 0;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (snapshot is not null)
        {
            SaveToDiskNoLock(snapshot);
        }
    }

    public bool Remove(string sectionName, string releaseName)
    {
        var key = DupeEntry.MakeKey(sectionName, releaseName);

        List<DupeEntry>? snapshot = null;

        _lock.EnterWriteLock();
        try
        {
            var removed = _entries.Remove(key);
            if (removed && ShouldPersist())
            {
                snapshot = _entries.Values.ToList();
                _pendingSaves = 0;
            }
            return removed;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (snapshot is not null)
        {
            SaveToDiskNoLock(snapshot);
        }

        return snapshot is not null;
    }

    private bool ShouldPersist()
    {
        var count = Interlocked.Increment(ref _pendingSaves);
        return count >= SaveBatchThreshold;
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<List<DupeEntry>>(json) ?? [];

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

    private void SaveToDiskNoLock(IReadOnlyCollection<DupeEntry> snapshot)
    {
        var json = JsonSerializer.Serialize(
            snapshot,
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