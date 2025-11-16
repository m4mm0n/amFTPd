using System.Security.Cryptography;
using System.Text;
using amFTPd.Utils;

namespace amFTPd.Db;

/// <summary>
/// Provides a thread-safe, encrypted, and persistent store for managing FTP sections.
/// </summary>
/// <remarks>The <see cref="BinarySectionStore"/> class is designed to store and manage FTP sections in a
/// binary database file. It supports operations such as adding, updating, retrieving, and deleting sections, while
/// ensuring data integrity through the use of a Write-Ahead Log (WAL). The database is encrypted using AES-GCM for
/// security, and changes are persisted atomically to prevent data corruption. The class also supports hot-reloading
/// of the database when changes are detected on disk.</remarks>
public sealed class BinarySectionStore : ISectionStore
{
    private readonly string _dbPath;
    private readonly string _walPath;
    private readonly byte[] _aesKey;

    private readonly Dictionary<string, FtpSection> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();
    private readonly WalFile _wal;
    private FileSystemWatcher? _watcher;

    public Action<string>? DebugLog;

    public BinarySectionStore(string dbPath, string masterPassword)
    {
        _dbPath = dbPath;
        _walPath = dbPath + ".wal";

        var salt = EnsureSalt(dbPath + ".salt");
        _aesKey = DeriveKey(masterPassword, salt);

        _wal = new WalFile(_walPath, _aesKey);

        if (File.Exists(dbPath))
            _sections = LoadDb();
        else
            _sections = CreateEmptyDb();

        ReplayWal();
        StartWatcher();
    }

    // =======================================================================
    // PUBLIC API
    // =======================================================================

    public FtpSection? FindSection(string s)
        => _sections.TryGetValue(s, out var v) ? v : null;

    public IEnumerable<FtpSection> GetAllSections()
        => _sections.Values;

    public bool TryAddSection(FtpSection s, out string? err)
    {
        lock (_sync)
        {
            if (_sections.ContainsKey(s.SectionName))
            {
                err = "Section exists.";
                return false;
            }

            _wal.Append(new WalEntry(WalEntryType.AddSection, BuildRecord(s)));
            _sections[s.SectionName] = s;

            if (_wal.NeedsCompaction())
                RewriteSnapshot();

            err = null;
            return true;
        }
    }

    public bool TryUpdateSection(FtpSection s, out string? err)
    {
        lock (_sync)
        {
            if (!_sections.ContainsKey(s.SectionName))
            {
                err = "Not found.";
                return false;
            }

            _wal.Append(new WalEntry(WalEntryType.UpdateSection, BuildRecord(s)));
            _sections[s.SectionName] = s;

            if (_wal.NeedsCompaction())
                RewriteSnapshot();

            err = null;
            return true;
        }
    }

    public bool TryDeleteSection(string name, out string? err)
    {
        lock (_sync)
        {
            if (!_sections.ContainsKey(name))
            {
                err = "Not found.";
                return false;
            }

            _wal.Append(new WalEntry(WalEntryType.DeleteSection, Encoding.UTF8.GetBytes(name)));
            _sections.Remove(name);

            if (_wal.NeedsCompaction())
                RewriteSnapshot();

            err = null;
            return true;
        }
    }

    public void ForceSnapshotRewrite()
    {
        lock (_sync)
        {
            WriteSnapshot(_sections); // Users or Groups or Sections
            _wal.Clear();
            DebugLog?.Invoke("[DB] Forced snapshot rewrite completed.");
        }
    }

    // =======================================================================
    // WAL REPLAY
    // =======================================================================

    private void ReplayWal()
    {
        foreach (var e in _wal.ReadAll())
        {
            switch (e.Type)
            {
                case WalEntryType.AddSection:
                {
                    var s = ParseRecord(e.Payload);
                    DebugLog?.Invoke($"[SECTION WAL] AddSection {s.SectionName}");
                    _sections[s.SectionName] = s;
                    break;
                }

                case WalEntryType.UpdateSection:
                {
                    var s = ParseRecord(e.Payload);
                    DebugLog?.Invoke($"[SECTION WAL] UpdateSection {s.SectionName}");
                    _sections[s.SectionName] = s;
                    break;
                }

                case WalEntryType.DeleteSection:
                {
                    var name = Encoding.UTF8.GetString(e.Payload);
                    DebugLog?.Invoke($"[SECTION WAL] DeleteSection {name}");
                    _sections.Remove(name);
                    break;
                }
            }
        }
    }

    // =======================================================================
    // DB LOADING
    // =======================================================================

    private Dictionary<string, FtpSection> LoadDb()
    {
        var enc = File.ReadAllBytes(_dbPath);
        var dec = Decrypt(enc);
        var raw = Lz4Codec.Decompress(dec);

        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);

        uint count = br.ReadUInt32();
        var dict = new Dictionary<string, FtpSection>(StringComparer.OrdinalIgnoreCase);

        for (uint i = 0; i < count; i++)
        {
            uint len = br.ReadUInt32();
            var rec = br.ReadBytes((int)len);
            var s = ParseRecord(rec);

            dict[s.SectionName] = s;
        }

        return dict;
    }

    private Dictionary<string, FtpSection> CreateEmptyDb()
    {
        DebugLog?.Invoke("[SECTION DB] Creating empty section DB...");

        var dict = new Dictionary<string, FtpSection>(StringComparer.OrdinalIgnoreCase);

        // Create classic default section "default"
        dict["default"] = new FtpSection(
            SectionName: "default",
            RelativePath: "/",
            UploadMultiplier: 1,
            DownloadMultiplier: 1,
            DefaultCreditsKb: 0
        );

        WriteSnapshot(dict);
        return dict;
    }

    // =======================================================================
    // SNAPSHOT
    // =======================================================================

    private void RewriteSnapshot()
    {
        DebugLog?.Invoke("[SECTION DB] WAL compaction triggered…");
        WriteSnapshot(_sections);
        _wal.Clear();
    }

    private void WriteSnapshot(Dictionary<string, FtpSection> dict)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((uint)dict.Count);

        foreach (var s in dict.Values)
        {
            var rec = BuildRecord(s);
            bw.Write((uint)rec.Length);
            bw.Write(rec);
        }

        var raw = ms.ToArray();
        var compressed = Lz4Codec.Compress(raw);
        var encrypted = Encrypt(compressed);

        AtomicSnapshot.WriteAtomic(_dbPath, encrypted);
    }

    // =======================================================================
    // RECORD FORMAT (v1, stable)
    // =======================================================================

    private byte[] BuildRecord(FtpSection s)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // lengths first
        var nameBytes = Encoding.UTF8.GetBytes(s.SectionName);
        var pathBytes = Encoding.UTF8.GetBytes(s.RelativePath);

        bw.Write((ushort)nameBytes.Length);   // nameLen
        bw.Write((ushort)pathBytes.Length);   // pathLen

        // data
        bw.Write(s.UploadMultiplier);
        bw.Write(s.DownloadMultiplier);
        bw.Write(s.DefaultCreditsKb);

        // strings
        bw.Write(nameBytes);
        bw.Write(pathBytes);

        return ms.ToArray();
    }

    private FtpSection ParseRecord(byte[] buf)
    {
        using var ms = new MemoryStream(buf);
        using var br = new BinaryReader(ms);

        string ReadStr(int len) => Encoding.UTF8.GetString(br.ReadBytes(len));

        ushort nameLen = br.ReadUInt16();
        ushort pathLen = br.ReadUInt16();

        long up = br.ReadInt64();
        long down = br.ReadInt64();
        long credits = br.ReadInt64();

        var name = ReadStr(nameLen);
        var path = ReadStr(pathLen);

        return new FtpSection(
            SectionName: name,
            RelativePath: path,
            UploadMultiplier: up,
            DownloadMultiplier: down,
            DefaultCreditsKb: credits
        );
    }

    // =======================================================================
    // ENCRYPTION
    // =======================================================================

    private byte[] Encrypt(byte[] buf)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[buf.Length];

        using (var gcm = new AesGcm(_aesKey))
            gcm.Encrypt(nonce, buf, ciphertext, tag);

        using var ms = new MemoryStream();
        ms.Write(nonce);
        ms.Write(ciphertext);
        ms.Write(tag);
        return ms.ToArray();
    }

    private byte[] Decrypt(byte[] enc)
    {
        ReadOnlySpan<byte> nonce = enc[..12];
        ReadOnlySpan<byte> tag = enc[^16..];
        ReadOnlySpan<byte> ciphertext = enc[12..^16];

        var buf = new byte[ciphertext.Length];

        using (var gcm = new AesGcm(_aesKey))
            gcm.Decrypt(nonce, ciphertext, tag, buf);

        return buf;
    }

    private static byte[] DeriveKey(string pw, byte[] salt)
    {
        using var pbk = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(pw),
            salt,
            200_000,
            HashAlgorithmName.SHA256);

        return pbk.GetBytes(32);
    }

    private static byte[] EnsureSalt(string path)
    {
        if (File.Exists(path))
            return File.ReadAllBytes(path);

        var salt = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, salt);
        return salt;
    }

    // =======================================================================
    // HOT RELOAD
    // =======================================================================

    private void StartWatcher()
    {
        var dir = Path.GetDirectoryName(_dbPath)!;
        var file = Path.GetFileName(_dbPath);

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite
        };

        _watcher.Changed += (_, __) =>
        {
            lock (_sync)
            {
                try
                {
                    DebugLog?.Invoke("[SECTION DB] Hot reload…");
                    var updated = LoadDb();
                    _sections.Clear();
                    foreach (var kv in updated)
                        _sections[kv.Key] = kv.Value;
                }
                catch
                {
                    DebugLog?.Invoke("[SECTION DB] Hot reload failed.");
                }
            }
        };

        _watcher.EnableRaisingEvents = true;
    }
}