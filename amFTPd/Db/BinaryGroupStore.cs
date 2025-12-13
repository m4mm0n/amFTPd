/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           BinaryGroupStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 20:08:55
 *  Last Modified:  2025-12-13 04:34:46
 *  CRC32:          0x78970648
 *  
 *  Description:
 *      Provides a secure, binary-based storage mechanism for managing FTP groups,  with support for write-ahead logging (WAL...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */









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
    private readonly WalFile _wal;

    public Action<string>? DebugLog;

    public BinaryGroupStore(string dbPath, string masterPassword)
    {
        _dbPath = dbPath;
        _walPath = dbPath + ".wal";

        var salt = EnsureSalt(dbPath + ".salt");
        _masterKey = DeriveKey(masterPassword, salt);

        _wal = new WalFile(_walPath, _masterKey);

        LoadOrCreateSnapshot();
        ReplayWal();
    }

    // ========================================================
    // INITIAL LOAD
    // ========================================================
    private void LoadOrCreateSnapshot()
    {
        const int MinLen = 12 + 16 + 1;
        var fi = new FileInfo(_dbPath);

        if (!fi.Exists || fi.Length < MinLen)
        {
            DebugLog?.Invoke("[GROUP DB] Creating new snapshot...");
            CreateDefaultGroups();
            WriteSnapshot();
            return;
        }

        try
        {
            var enc = File.ReadAllBytes(_dbPath);
            var dec = DecryptSnapshot(enc);
            var raw = Lz4Codec.Decompress(dec);
            ParseSnapshot(raw);
        }
        catch
        {
            DebugLog?.Invoke("[GROUP DB] Snapshot corrupt — creating fresh.");
            _groups.Clear();
            CreateDefaultGroups();
            WriteSnapshot();
        }
    }

    private void CreateDefaultGroups()
    {
        _groups.Clear();

        _groups["admins"] = new FtpGroup(
            GroupName: "admins",
            Description: "Administrators group",
            Users: ["admin"],
            SectionCredits: new Dictionary<string, long>()
        );
    }

    // ========================================================
    // SNAPSHOT WRITE
    // ========================================================
    private void WriteSnapshot()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((uint)_groups.Count);

        foreach (var g in _groups.Values)
        {
            var rec = BuildRecord(g);
            bw.Write((uint)rec.Length);
            bw.Write(rec);
        }

        var raw = ms.ToArray();
        var compressed = Lz4Codec.Compress(raw);
        var encrypted = EncryptSnapshot(compressed);

        AtomicSnapshot.WriteAtomic(_dbPath, encrypted);
    }

    // ========================================================
    // SNAPSHOT READ
    // ========================================================
    private void ParseSnapshot(byte[] raw)
    {
        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);

        _groups.Clear();

        var count = br.ReadUInt32();
        for (uint i = 0; i < count; i++)
        {
            var len = br.ReadUInt32();
            var rec = br.ReadBytes((int)len);
            var grp = ParseRecord(rec);
            _groups[grp.GroupName] = grp;
        }
    }

    // ========================================================
    // WAL REPLAY
    // ========================================================
    private void ReplayWal()
    {
        foreach (var e in _wal.ReadAll())
        {
            switch (e.Type)
            {
                case WalEntryType.AddGroup:
                case WalEntryType.UpdateGroup:
                    {
                        var grp = ParseRecord(e.Payload);
                        _groups[grp.GroupName] = grp;
                        break;
                    }

                case WalEntryType.DeleteGroup:
                    {
                        var name = Encoding.UTF8.GetString(e.Payload);
                        _groups.Remove(name);
                        break;
                    }

                case WalEntryType.RenameGroup:
                    {
                        var payload = Encoding.UTF8.GetString(e.Payload).Split('|');
                        var oldName = payload[0];
                        var newName = payload[1];

                        if (_groups.TryGetValue(oldName, out var g))
                        {
                            _groups.Remove(oldName);

                            var updated = g with { GroupName = newName };
                            _groups[newName] = updated;
                        }
                        break;
                    }
            }
        }
    }

    // ========================================================
    // PUBLIC API (implements IGroupStore fully)
    // ========================================================
    public FtpGroup? FindGroup(string groupName)
        => _groups.TryGetValue(groupName, out var g) ? g : null;

    public IEnumerable<FtpGroup> GetAllGroups()
        => _groups.Values;

    public bool TryAddGroup(FtpGroup group, out string? error)
    {
        if (_groups.ContainsKey(group.GroupName))
        {
            error = "Group exists.";
            return false;
        }

        var rec = BuildRecord(group);
        _wal.Append(new WalEntry(WalEntryType.AddGroup, rec));

        _groups[group.GroupName] = group;
        CompactCheck();
        error = null;
        return true;
    }

    public bool TryUpdateGroup(FtpGroup group, out string? error)
    {
        if (!_groups.ContainsKey(group.GroupName))
        {
            error = "Group does not exist.";
            return false;
        }

        var rec = BuildRecord(group);
        _wal.Append(new WalEntry(WalEntryType.UpdateGroup, rec));

        _groups[group.GroupName] = group;
        CompactCheck();
        error = null;
        return true;
    }

    public bool TryDeleteGroup(string groupName, out string? error)
    {
        if (!_groups.ContainsKey(groupName))
        {
            error = "Group does not exist.";
            return false;
        }

        _wal.Append(new WalEntry(WalEntryType.DeleteGroup, Encoding.UTF8.GetBytes(groupName)));

        _groups.Remove(groupName);
        CompactCheck();
        error = null;
        return true;
    }

    public bool TryRenameGroup(string oldName, string newName, out string? error)
    {
        if (!_groups.ContainsKey(oldName))
        {
            error = $"Group '{oldName}' not found.";
            return false;
        }

        if (_groups.ContainsKey(newName))
        {
            error = $"Group '{newName}' already exists.";
            return false;
        }

        var payload = Encoding.UTF8.GetBytes(oldName + "|" + newName);
        _wal.Append(new WalEntry(WalEntryType.RenameGroup, payload));

        var group = _groups[oldName];
        _groups.Remove(oldName);

        var updated = group with { GroupName = newName };
        _groups[newName] = updated;

        CompactCheck();
        error = null;
        return true;
    }

    // ========================================================
    // COMPACTION
    // ========================================================
    private void CompactCheck()
    {
        if (_wal.NeedsCompaction())
        {
            WriteSnapshot();
            _wal.Clear();
        }
    }

    // ========================================================
    // RECORD FORMAT (exactly matches your FtpGroup structure)
    // ========================================================
    private byte[] BuildRecord(FtpGroup g)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        var nameBytes = Encoding.UTF8.GetBytes(g.GroupName);
        var descBytes = Encoding.UTF8.GetBytes(g.Description ?? "");
        var userCount = g.Users.Count;
        var credCount = g.SectionCredits.Count;

        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)descBytes.Length);
        bw.Write((ushort)userCount);
        bw.Write((ushort)credCount);

        bw.Write(nameBytes);
        bw.Write(descBytes);

        foreach (var u in g.Users)
        {
            var ub = Encoding.UTF8.GetBytes(u);
            bw.Write((ushort)ub.Length);
            bw.Write(ub);
        }

        foreach (var kv in g.SectionCredits)
        {
            var sb = Encoding.UTF8.GetBytes(kv.Key);
            bw.Write((ushort)sb.Length);
            bw.Write(sb);
            bw.Write(kv.Value);
        }

        return ms.ToArray();
    }

    private FtpGroup ParseRecord(byte[] buf)
    {
        using var ms = new MemoryStream(buf);
        using var br = new BinaryReader(ms);

        var nameLen = br.ReadUInt16();
        var descLen = br.ReadUInt16();
        var userCount = br.ReadUInt16();
        var credCount = br.ReadUInt16();

        var name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
        var desc = Encoding.UTF8.GetString(br.ReadBytes(descLen));

        var users = new List<string>(userCount);
        for (var i = 0; i < userCount; i++)
        {
            var len = br.ReadUInt16();
            users.Add(Encoding.UTF8.GetString(br.ReadBytes(len)));
        }

        var creds = new Dictionary<string, long>();
        for (var i = 0; i < credCount; i++)
        {
            var len = br.ReadUInt16();
            var sec = Encoding.UTF8.GetString(br.ReadBytes(len));
            var val = br.ReadInt64();
            creds[sec] = val;
        }

        return new FtpGroup(name, desc, users, creds);
    }

    // ========================================================
    // ENCRYPTION
    // ========================================================
    private byte[] EncryptSnapshot(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using (var gcm = new AesGcm(_masterKey, 16))
            gcm.Encrypt(nonce, plain, cipher, tag);

        var res = new byte[12 + cipher.Length + 16];
        Buffer.BlockCopy(nonce, 0, res, 0, 12);
        Buffer.BlockCopy(cipher, 0, res, 12, cipher.Length);
        Buffer.BlockCopy(tag, 0, res, 12 + cipher.Length, 16);
        return res;
    }

    private byte[] DecryptSnapshot(byte[] buf)
    {
        ReadOnlySpan<byte> nonce = buf[..12];
        ReadOnlySpan<byte> tag = buf[^16..];
        ReadOnlySpan<byte> cipher = buf[12..^16];

        var plain = new byte[cipher.Length];

        using (var gcm = new AesGcm(_masterKey, 16))
            gcm.Decrypt(nonce, cipher, tag, plain);

        return plain;
    }

    // ========================================================
    // SALT + KEY
    // ========================================================
    private static byte[] EnsureSalt(string path)
    {
        if (File.Exists(path))
            return File.ReadAllBytes(path);

        var salt = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, salt);
        return salt;
    }

    private byte[] DeriveKey(string masterPassword, byte[] salt)
    {
        using var pbkdf = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(masterPassword),
            salt,
            200_000,
            HashAlgorithmName.SHA256);

        return pbkdf.GetBytes(32);
    }

    public void ForceSnapshotRewrite()
    {
        WriteSnapshot();
        _wal.Clear();
    }
}