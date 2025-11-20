/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using System.Security.Cryptography;
using System.Text;
using amFTPd.Utils;

namespace amFTPd.Db;

/// <summary>
/// Provides a secure, binary-based storage mechanism for managing FTP groups,  with support for write-ahead logging
/// (WAL) and hot-reloading capabilities.
/// </summary>
/// <remarks>This class is designed to store and manage FTP group data in a binary format,  ensuring data
/// integrity through encryption and write-ahead logging. It supports  operations such as adding, updating, and
/// deleting groups, and provides mechanisms  for replaying WAL entries and compacting the database. The class is
/// thread-safe  for concurrent access to group operations. <para> The database is encrypted using a master
/// password, and changes are logged to a  write-ahead log file to ensure durability. A file system watcher is used
/// to  enable hot-reloading of the database when external changes are detected. </para></remarks>
public sealed class BinaryGroupStore : IGroupStore
{
    private readonly string _dbPath;
    private readonly string _walPath;
    private readonly byte[] _masterKey;
    private readonly Dictionary<string, FtpGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();
    private readonly WalFile _wal;

    private FileSystemWatcher? _watcher;
    public Action<string>? DebugLog;

    public BinaryGroupStore(string dbPath, string masterPassword)
    {
        _dbPath = dbPath;
        _walPath = dbPath + ".wal";

        var salted = EnsureSaltFile(dbPath + ".salt");
        _masterKey = DeriveKey(masterPassword, salted);

        _wal = new WalFile(_walPath, _masterKey);

        _groups = File.Exists(dbPath) ? LoadDb() : CreateEmptyDb();

        ReplayWal();
        StartWatcher();
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    public FtpGroup? FindGroup(string g)
        => _groups.TryGetValue(g, out var grp) ? grp : null;

    public IEnumerable<FtpGroup> GetAllGroups()
        => _groups.Values;

    public bool TryAddGroup(FtpGroup g, out string? err)
    {
        lock (_sync)
        {
            if (_groups.ContainsKey(g.GroupName))
            {
                err = "Group exists.";
                return false;
            }

            var rec = BuildRecord(g);
            _wal.Append(new WalEntry(WalEntryType.AddGroup, rec));
            _groups[g.GroupName] = g;

            if (_wal.NeedsCompaction())
                RewriteSnapshot();

            err = null;
            return true;
        }
    }

    public bool TryUpdateGroup(FtpGroup g, out string? err)
    {
        lock (_sync)
        {
            if (!_groups.ContainsKey(g.GroupName))
            {
                err = "Not found.";
                return false;
            }

            var rec = BuildRecord(g);
            _wal.Append(new WalEntry(WalEntryType.UpdateGroup, rec));
            _groups[g.GroupName] = g;

            if (_wal.NeedsCompaction())
                RewriteSnapshot();

            err = null;
            return true;
        }
    }

    public bool TryDeleteGroup(string name, out string? err)
    {
        lock (_sync)
        {
            if (!_groups.ContainsKey(name))
            {
                err = "Not found.";
                return false;
            }

            var nameBytes = Encoding.UTF8.GetBytes(name);
            _wal.Append(new WalEntry(WalEntryType.DeleteGroup, nameBytes));

            _groups.Remove(name);

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
            WriteSnapshot(_groups); // Users or Groups or Sections
            _wal.Clear();
            DebugLog?.Invoke("[DB] Forced snapshot rewrite completed.");
        }
    }

    public bool TryRenameGroup(string oldName, string newName, out string? error)
    {
        lock (_sync)
        {
            if (!_groups.ContainsKey(oldName))
            {
                error = $"Group '{oldName}' not found.";
                return false;
            }

            if (_groups.ContainsKey(newName))
            {
                error = $"A group named '{newName}' already exists.";
                return false;
            }

            // Fetch group
            var g = _groups[oldName];

            // Create updated group record
            var updated = g with { GroupName = newName };

            // WAL: group rename = DeleteGroup(oldName) + AddGroup(newName)
            // because WAL entries do not have a Rename opcode.
            //
            // This keeps the WAL format *simple* and makes replay trivial.
            //
            _wal.Append(new WalEntry(
                WalEntryType.DeleteGroup,
                Encoding.UTF8.GetBytes(oldName)
            ));

            var record = BuildRecord(updated);
            _wal.Append(new WalEntry(
                WalEntryType.AddGroup,
                record
            ));

            // Update memory
            _groups.Remove(oldName);
            _groups[newName] = updated;

            DebugLog?.Invoke($"[GROUP-DB] Rename '{oldName}' → '{newName}'");

            // Check compaction
            if (_wal.NeedsCompaction())
                RewriteSnapshot();

            error = null;
            return true;
        }
    }

    // ============================================================
    // WAL REPLAY
    // ============================================================

    private void ReplayWal()
    {
        foreach (var e in _wal.ReadAll())
        {
            switch (e.Type)
            {
                case WalEntryType.AddGroup:
                {
                    var g = ParseRecord(e.Payload);
                    DebugLog?.Invoke($"[GROUP WAL] AddGroup {g.GroupName}");
                    _groups[g.GroupName] = g;
                    break;
                }
                case WalEntryType.UpdateGroup:
                {
                    var g = ParseRecord(e.Payload);
                    DebugLog?.Invoke($"[GROUP WAL] UpdateGroup {g.GroupName}");
                    _groups[g.GroupName] = g;
                    break;
                }
                case WalEntryType.DeleteGroup:
                {
                    var name = Encoding.UTF8.GetString(e.Payload);
                    DebugLog?.Invoke($"[GROUP WAL] DeleteGroup {name}");
                    _groups.Remove(name);
                    break;
                }
            }
        }
    }

    // ============================================================
    // DB LOADING
    // ============================================================

    private Dictionary<string, FtpGroup> LoadDb()
    {
        var enc = File.ReadAllBytes(_dbPath);
        var dec = Decrypt(enc);
        var raw = Lz4Codec.Decompress(dec);

        var dict = new Dictionary<string, FtpGroup>(StringComparer.OrdinalIgnoreCase);

        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);

        var count = br.ReadUInt32();

        for (uint i = 0; i < count; i++)
        {
            var len = br.ReadUInt32();
            var rec = br.ReadBytes((int)len);

            var g = ParseRecord(rec);
            dict[g.GroupName] = g;
        }

        return dict;
    }

    private Dictionary<string, FtpGroup> CreateEmptyDb()
    {
        DebugLog?.Invoke("[GROUP DB] Creating empty group database...");

        var dict = new Dictionary<string, FtpGroup>(StringComparer.OrdinalIgnoreCase);

        // Default "admins" group
        dict["admins"] = new FtpGroup(
            GroupName: "admins",
            Description: "Builtin administrator group",
            Users: new List<string>() { "admin" },
            SectionCredits: new Dictionary<string, long>()
        );

        WriteSnapshot(dict);

        return dict;
    }

    // ============================================================
    // SNAPSHOT WRITE
    // ============================================================

    private void RewriteSnapshot()
    {
        DebugLog?.Invoke("[GROUP DB] WAL compaction triggered...");
        WriteSnapshot(_groups);
        _wal.Clear();
    }

    private void WriteSnapshot(Dictionary<string, FtpGroup> dict)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((uint)dict.Count);

        foreach (var g in dict.Values)
        {
            var rec = BuildRecord(g);
            bw.Write((uint)rec.Length);
            bw.Write(rec);
        }

        var raw = ms.ToArray();
        var compressed = Lz4Codec.Compress(raw);
        var encrypted = Encrypt(compressed);

        AtomicSnapshot.WriteAtomic(_dbPath, encrypted);
    }

    // ============================================================
    // RECORD FORMAT (v1)
    // ============================================================

    private byte[] BuildRecord(FtpGroup g)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // group name
        var gName = Encoding.UTF8.GetBytes(g.GroupName);
        bw.Write((ushort)gName.Length);

        // description
        var descBytes = Encoding.UTF8.GetBytes(g.Description ?? "");
        bw.Write((ushort)descBytes.Length);

        // users
        bw.Write((ushort)g.Users.Count);
        foreach (var u in g.Users)
        {
            var ub = Encoding.UTF8.GetBytes(u);
            bw.Write((ushort)ub.Length);
            bw.Write(ub);
        }

        // SectionCredits
        bw.Write((ushort)g.SectionCredits.Count);
        foreach (var kv in g.SectionCredits)
        {
            var keyBytes = Encoding.UTF8.GetBytes(kv.Key);
            bw.Write((ushort)keyBytes.Length);
            bw.Write(keyBytes);
            bw.Write(kv.Value);
        }

        // now actual data:
        bw.Write(gName);
        bw.Write(descBytes);

        foreach (var u in g.Users)
            bw.Write(Encoding.UTF8.GetBytes(u));

        foreach (var kv in g.SectionCredits)
        {
            bw.Write(Encoding.UTF8.GetBytes(kv.Key));
            bw.Write(kv.Value);
        }

        return ms.ToArray();
    }

    private FtpGroup ParseRecord(byte[] buf)
    {
        using var ms = new MemoryStream(buf);
        using var br = new BinaryReader(ms);

        string ReadStr(ushort len) => Encoding.UTF8.GetString(br.ReadBytes(len));

        // read lengths first
        var nameLen = br.ReadUInt16();
        var descLen = br.ReadUInt16();

        var userCount = br.ReadUInt16();
        var users = new List<string>(userCount);

        var userLens = new ushort[userCount];
        for (var i = 0; i < userCount; i++)
            userLens[i] = br.ReadUInt16();

        var secCount = br.ReadUInt16();
        var secLens = new ushort[secCount];
        for (var i = 0; i < secCount; i++)
            secLens[i] = br.ReadUInt16();

        // strings
        var gName = ReadStr(nameLen);
        var desc = ReadStr(descLen);

        for (var i = 0; i < userCount; i++)
            users.Add(ReadStr(userLens[i]));

        var credits = new Dictionary<string, long>(secCount, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < secCount; i++)
        {
            var key = ReadStr(secLens[i]);
            var val = br.ReadInt64();
            credits[key] = val;
        }

        return new FtpGroup(
            GroupName: gName,
            Description: desc,
            Users: users,
            SectionCredits: credits
        );
    }

    // ============================================================
    // ENCRYPTION
    // ============================================================

    private byte[] Encrypt(byte[] buf)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[buf.Length];

        using (var gcm = new AesGcm(_masterKey))
            gcm.Encrypt(nonce, buf, cipher, tag);

        using var ms = new MemoryStream();
        ms.Write(nonce);
        ms.Write(cipher);
        ms.Write(tag);
        return ms.ToArray();
    }

    private byte[] Decrypt(byte[] enc)
    {
        ReadOnlySpan<byte> nonce = enc[..12];
        ReadOnlySpan<byte> tag = enc[^16..];
        ReadOnlySpan<byte> ciphertext = enc[12..^16];

        var buf = new byte[ciphertext.Length];

        using (var gcm = new AesGcm(_masterKey))
            gcm.Decrypt(nonce, ciphertext, tag, buf);

        return buf;
    }

    private byte[] DeriveKey(string pw, byte[] salt)
    {
        using var pbk = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(pw),
            salt,
            200_000,
            HashAlgorithmName.SHA256);

        return pbk.GetBytes(32);
    }

    private static byte[] EnsureSaltFile(string path)
    {
        if (File.Exists(path))
            return File.ReadAllBytes(path);

        var salt = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, salt);
        return salt;
    }

    // ============================================================
    // HOT RELOAD
    // ============================================================

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
                    DebugLog?.Invoke("[GROUP DB] Hot reload…");
                    var updated = LoadDb();
                    _groups.Clear();
                    foreach (var kv in updated)
                        _groups[kv.Key] = kv.Value;
                }
                catch
                {
                    DebugLog?.Invoke("[GROUP DB] Hot reload failed.");
                }
            }
        };

        _watcher.EnableRaisingEvents = true;
    }
}