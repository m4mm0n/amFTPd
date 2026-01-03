using System.Text;
using System.Text.RegularExpressions;

namespace amFTPd.Core.Dupe;

/// <summary>
/// Provides a persistent, thread-safe store for duplicate entries using a binary file-based backend.
/// </summary>
/// <remarks>BinaryDupeStore maintains duplicate entry data on disk, allowing efficient lookup, insertion, and
/// removal operations across application restarts. All operations are thread-safe. Instances of this class should be
/// disposed when no longer needed to release file handles and system resources.</remarks>
public sealed class BinaryDupeStore : IDupeStore, IDisposable
{
    private readonly FileStream _meta;
    private readonly FileStream _crc;
    private readonly FileStream _idx;

    private readonly Dictionary<string, long> _index =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ReaderWriterLockSlim _lock = new();

    public BinaryDupeStore(string baseDir)
    {
        Directory.CreateDirectory(baseDir);

        _meta = Open(baseDir, "dupes.meta");
        _crc = Open(baseDir, "dupes.crc");
        _idx = Open(baseDir, "dupes.idx");

        LoadIndex();
    }

    private static FileStream Open(string dir, string name)
        => new(Path.Combine(dir, name),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read);

    // =====================================================================
    // Public API (scene-level)
    // =====================================================================

    public void AddOrUpdateFile(
        string section,
        string release,
        string fileName,
        uint crc32,
        long fileSize,
        string uploaderUser,
        string? uploaderGroup)
    {
        var key = DupeEntry.MakeKey(section, release);
        var now = DateTimeOffset.UtcNow;

        _lock.EnterWriteLock();
        try
        {
            BinaryDupeMetaRecord meta;
            List<BinaryDupeCrcEntry> files;

            if (_index.TryGetValue(key, out var offset))
            {
                meta = ReadMeta(offset);
                files = ReadCrcList(meta);
            }
            else
            {
                meta = new BinaryDupeMetaRecord
                {
                    Section = section,
                    Release = release,
                    Group = uploaderGroup ?? "",
                    FirstSeenUnix = now.ToUnixTimeSeconds(),
                    TotalBytes = 0
                };
                files = new List<BinaryDupeCrcEntry>();
            }

            // Remove existing entry for same filename
            var removed = files.RemoveAll(f =>
                f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
            {
                meta.FileCount++;
                meta.TotalBytes += fileSize;

                if (IsArchive(fileName))
                    meta.ArchiveCount++;
            }

            files.Add(new BinaryDupeCrcEntry(fileName, crc32));

            meta.LastUpdatedUnix = now.ToUnixTimeSeconds();

            WriteRelease(section, release, meta, files);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void RemoveFile(string section, string release, string fileName)
    {
        var key = DupeEntry.MakeKey(section, release);

        _lock.EnterWriteLock();
        try
        {
            if (!_index.TryGetValue(key, out var offset))
                return;

            var meta = ReadMeta(offset);
            var files = ReadCrcList(meta);

            var removed = files.RemoveAll(f =>
                f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
                return;

            meta.FileCount = Math.Max(0, meta.FileCount - removed);
            meta.ArchiveCount = files.Count(IsArchiveEntry);

            if (files.Count == 0)
            {
                _index.Remove(key);
                PersistIndex();
                return;
            }

            meta.LastUpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WriteRelease(section, release, meta, files);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // =====================================================================
    // IDupeStore (legacy scene interface)
    // =====================================================================

    public DupeEntry? Find(string section, string release)
    {
        var key = DupeEntry.MakeKey(section, release);

        _lock.EnterReadLock();
        try
        {
            if (!_index.TryGetValue(key, out var offset))
                return null;

            var meta = ReadMeta(offset);

            return new DupeEntry
            {
                SectionName = meta.Section,
                ReleaseName = meta.Release,
                UploaderGroup = meta.Group,
                TotalBytes = meta.TotalBytes,
                FirstSeen = DateTimeOffset.FromUnixTimeSeconds(meta.FirstSeenUnix),
                LastUpdated = DateTimeOffset.FromUnixTimeSeconds(meta.LastUpdatedUnix),
                IsNuked = meta.IsNuked,
                NukeMultiplier = (int)meta.NukeMultiplier,
                NukeReason = meta.NukeReason
            };
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<DupeEntry> Search(
        string pattern,
        string? sectionName = null,
        int limit = 50)
    {
        var rx = WildcardToRegex(pattern);
        var list = new List<DupeEntry>(limit);

        _lock.EnterReadLock();
        try
        {
            foreach (var (key, offset) in _index)
            {
                if (list.Count >= limit)
                    break;

                var meta = ReadMeta(offset);

                if (sectionName != null &&
                    !meta.Section.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!rx.IsMatch(meta.Release))
                    continue;

                list.Add(new DupeEntry
                {
                    SectionName = meta.Section,
                    ReleaseName = meta.Release,
                    UploaderGroup = meta.Group,
                    TotalBytes = meta.TotalBytes,
                    FirstSeen = DateTimeOffset.FromUnixTimeSeconds(meta.FirstSeenUnix),
                    LastUpdated = DateTimeOffset.FromUnixTimeSeconds(meta.LastUpdatedUnix),
                    IsNuked = meta.IsNuked,
                    NukeMultiplier = (int)meta.NukeMultiplier,
                    NukeReason = meta.NukeReason
                });
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return list;
    }

    // =====================================================================
    // IDupeStore compatibility (legacy metadata-only)
    // =====================================================================

    void IDupeStore.Upsert(DupeEntry entry)
    {
        if (entry is null)
            throw new ArgumentNullException(nameof(entry));

        var key = DupeEntry.MakeKey(entry.SectionName, entry.ReleaseName);
        var now = DateTimeOffset.UtcNow;

        _lock.EnterWriteLock();
        try
        {
            BinaryDupeMetaRecord meta;
            List<BinaryDupeCrcEntry> files;

            if (_index.TryGetValue(key, out var offset))
            {
                meta = ReadMeta(offset);
                files = ReadCrcList(meta);
            }
            else
            {
                meta = new BinaryDupeMetaRecord
                {
                    Section = entry.SectionName,
                    Release = entry.ReleaseName,
                    Group = entry.UploaderGroup ?? "",
                    FirstSeenUnix = entry.FirstSeen.ToUnixTimeSeconds()
                };
                files = new List<BinaryDupeCrcEntry>();
            }

            meta.TotalBytes = entry.TotalBytes;
            meta.LastUpdatedUnix = now.ToUnixTimeSeconds();
            meta.IsNuked = entry.IsNuked;
            meta.NukeMultiplier = entry.NukeMultiplier;
            meta.NukeReason = entry.NukeReason;

            WriteRelease(entry.SectionName, entry.ReleaseName, meta, files);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    bool IDupeStore.Remove(string section, string release)
    {
        var key = DupeEntry.MakeKey(section, release);

        _lock.EnterWriteLock();
        try
        {
            var removed = _index.Remove(key);
            if (removed)
                PersistIndex();
            return removed;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // =====================================================================
    // Internal storage
    // =====================================================================

    private void WriteRelease(
        string section,
        string release,
        BinaryDupeMetaRecord meta,
        List<BinaryDupeCrcEntry> files)
    {
        meta.CrcOffset = _crc.Length;
        meta.CrcCount = files.Count;

        _crc.Seek(meta.CrcOffset, SeekOrigin.Begin);
        using (var bw = new BinaryWriter(_crc, Encoding.UTF8, true))
        {
            foreach (var f in files)
            {
                bw.Write(f.FileName);
                bw.Write(f.Crc);
            }
        }

        var key = DupeEntry.MakeKey(section, release);
        var offset = _meta.Length;

        WriteMeta(meta, offset);
        _index[key] = offset;
        PersistIndex();
    }

    private List<BinaryDupeCrcEntry> ReadCrcList(BinaryDupeMetaRecord meta)
    {
        var list = new List<BinaryDupeCrcEntry>(meta.CrcCount);

        _crc.Seek(meta.CrcOffset, SeekOrigin.Begin);
        using var br = new BinaryReader(_crc, Encoding.UTF8, true);

        for (var i = 0; i < meta.CrcCount; i++)
        {
            var name = br.ReadString();
            var crc = br.ReadUInt32();
            list.Add(new BinaryDupeCrcEntry(name, crc));
        }

        return list;
    }

    private void WriteMeta(BinaryDupeMetaRecord m, long offset)
    {
        _meta.Seek(offset, SeekOrigin.Begin);
        using var bw = new BinaryWriter(_meta, Encoding.UTF8, true);

        bw.Write(m.Section);
        bw.Write(m.Release);
        bw.Write(m.Group);

        bw.Write(m.TotalBytes);
        bw.Write(m.FileCount);
        bw.Write(m.ArchiveCount);

        bw.Write(m.FirstSeenUnix);
        bw.Write(m.LastUpdatedUnix);

        bw.Write(m.IsNuked);
        bw.Write(m.NukeMultiplier);
        bw.Write(m.NukeReason ?? "");

        bw.Write(m.CrcOffset);
        bw.Write(m.CrcCount);
    }

    private BinaryDupeMetaRecord ReadMeta(long offset)
    {
        _meta.Seek(offset, SeekOrigin.Begin);
        using var br = new BinaryReader(_meta, Encoding.UTF8, true);

        return new BinaryDupeMetaRecord
        {
            Section = br.ReadString(),
            Release = br.ReadString(),
            Group = br.ReadString(),

            TotalBytes = br.ReadInt64(),
            FileCount = br.ReadInt32(),
            ArchiveCount = br.ReadInt32(),

            FirstSeenUnix = br.ReadInt64(),
            LastUpdatedUnix = br.ReadInt64(),

            IsNuked = br.ReadBoolean(),
            NukeMultiplier = br.ReadDouble(),
            NukeReason = br.ReadString(),

            CrcOffset = br.ReadInt64(),
            CrcCount = br.ReadInt32()
        };
    }

    // =====================================================================
    // Index
    // =====================================================================

    private void LoadIndex()
    {
        if (_idx.Length == 0) return;

        _idx.Seek(0, SeekOrigin.Begin);
        using var br = new BinaryReader(_idx, Encoding.UTF8, true);

        var count = br.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var key = br.ReadString();
            var off = br.ReadInt64();
            _index[key] = off;
        }
    }

    private void PersistIndex()
    {
        _idx.SetLength(0);
        _idx.Seek(0, SeekOrigin.Begin);

        using var bw = new BinaryWriter(_idx, Encoding.UTF8, true);
        bw.Write(_index.Count);

        foreach (var kv in _index)
        {
            bw.Write(kv.Key);
            bw.Write(kv.Value);
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static bool IsArchive(string name)
    {
        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext)) return false;

        ext = ext.ToLowerInvariant();

        return ext is ".zip" or ".rar" or ".7z" or ".ace" or ".arj" or ".lha" or ".lzh"
            || Regex.IsMatch(ext, @"\.r\d\d")
            || Regex.IsMatch(ext, @"\.\d\d\d");
    }

    private static bool IsArchiveEntry(BinaryDupeCrcEntry e)
        => IsArchive(e.FileName);

    private static Regex WildcardToRegex(string pattern)
    {
        var esc = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return new Regex("^" + esc + "$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public void Dispose()
    {
        _meta.Dispose();
        _crc.Dispose();
        _idx.Dispose();
        _lock.Dispose();
    }
}