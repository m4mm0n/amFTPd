/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RaceEngine.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xBF72F6AC
 *  
 *  Description:
 *      Tracks race stats per release directory. Thread-safe, in-memory only.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core.Race;

/// <summary>
/// Tracks race stats per release directory. Thread-safe, in-memory only.
/// </summary>
public sealed class RaceEngine
{
    private sealed class RaceState
    {
        public string ReleasePath { get; }
        public string SectionName { get; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset LastUpdatedAt { get; set; }
        public Dictionary<string, long> UserBytes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public long TotalBytes { get; set; }
        public int FileCount { get; set; }

        public RaceState(string releasePath, string sectionName)
        {
            ReleasePath = releasePath;
            SectionName = sectionName;
        }

        public RaceSnapshot ToSnapshot() =>
            new(
                ReleasePath,
                SectionName,
                StartedAt,
                LastUpdatedAt,
                new Dictionary<string, long>(UserBytes),
                TotalBytes,
                FileCount
            );
    }

    private const int MaxHistoryEntries = 100;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, RaceState> _races = new(StringComparer.OrdinalIgnoreCase);

    private readonly LinkedList<string> _recentKeys = new();
    private readonly HashSet<string> _recentSet = new(StringComparer.OrdinalIgnoreCase);

    private void TouchKey(string key)
    {
        // key is already normalized
        if (_recentSet.Add(key))
        {
            _recentKeys.AddFirst(key);
            if (_recentKeys.Count > MaxHistoryEntries)
            {
                var last = _recentKeys.Last!;
                _recentSet.Remove(last.Value);
                _recentKeys.RemoveLast();
            }
        }
        else
        {
            // move to front
            var node = _recentKeys.Find(key);
            if (node is not null && node != _recentKeys.First)
            {
                _recentKeys.Remove(node);
                _recentKeys.AddFirst(node);
            }
        }
    }

    /// <summary>
    /// Retrieves a list of the most recent race snapshots, up to the specified maximum count.
    /// </summary>
    /// <param name="maxCount">The maximum number of recent race snapshots to return. If less than or equal to zero, a single snapshot is
    /// returned.</param>
    /// <returns>An immutable list containing up to the specified number of recent race snapshots. The list may contain fewer
    /// entries if fewer races are available.</returns>
    public IReadOnlyList<RaceSnapshot> GetRecentRaces(int maxCount)
    {
        if (maxCount <= 0) maxCount = 1;

        var snapshots = new List<RaceSnapshot>(Math.Min(maxCount, MaxHistoryEntries));

        lock (_sync)
        {
            foreach (var key in _recentKeys)
            {
                if (!_races.TryGetValue(key, out var state))
                    continue;

                snapshots.Add(state.ToSnapshot());
                if (snapshots.Count >= maxCount)
                    break;
            }
        }

        return snapshots;
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        path = path.Replace('\\', '/');
        if (!path.StartsWith("/"))
            path = "/" + path;

        // no trailing slash semantics for key
        if (path.Length > 1 && path.EndsWith("/"))
            path = path.TrimEnd('/');

        return path;
    }

    /// <summary>
    /// Register an upload for the given user and release directory.
    /// </summary>
    public RaceSnapshot RegisterUpload(string userName, string releasePath, string sectionName, long bytes)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("User name cannot be empty.", nameof(userName));

        if (bytes <= 0)
        {
            // nothing to track, but return current snapshot if any
            if (TryGetRace(releasePath, out var existing))
                return existing;

            var now = DateTimeOffset.UtcNow;
            return new RaceSnapshot(
                Normalize(releasePath),
                sectionName,
                now,
                now,
                new Dictionary<string, long>(),
                0,
                0
            );
        }

        releasePath = Normalize(releasePath);
        var nowTs = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            if (!_races.TryGetValue(releasePath, out var state))
            {
                state = new RaceState(releasePath, sectionName)
                {
                    StartedAt = nowTs
                };
                _races[releasePath] = state;
            }

            state.LastUpdatedAt = nowTs;
            state.TotalBytes += bytes;
            state.FileCount++;

            if (!state.UserBytes.TryGetValue(userName, out var prev))
                prev = 0;

            state.UserBytes[userName] = prev + bytes;

            TouchKey(releasePath);

            return state.ToSnapshot();
        }
    }

    /// <summary>
    /// Try to get race stats for a release directory.
    /// </summary>
    public bool TryGetRace(string releasePath, out RaceSnapshot snapshot)
    {
        releasePath = Normalize(releasePath);
        lock (_sync)
        {
            if (_races.TryGetValue(releasePath, out var state))
            {
                snapshot = state.ToSnapshot();
                return true;
            }
        }

        snapshot = default!;
        return false;
    }
}