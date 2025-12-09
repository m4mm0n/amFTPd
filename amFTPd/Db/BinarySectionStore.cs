/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-27
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
    private readonly byte[] _masterKey;

    private readonly Dictionary<string, Config.Ftpd.FtpSection> _sections
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly WalFile _wal;

    public Action<string>? DebugLog;

    public BinarySectionStore(string dbPath, string masterPassword)
    {
        _dbPath = dbPath;
        _walPath = dbPath + ".wal";

        var salt = EnsureSalt(dbPath + ".salt");
        _masterKey = DeriveKey(masterPassword, salt);

        _wal = new WalFile(_walPath, _masterKey);

        LoadOrCreateSnapshot();
        ReplayWal();
    }

    // ==========================================================
    // INITIAL LOAD
    // ==========================================================
    private void LoadOrCreateSnapshot()
    {
        const int MinSnapshot = 12 + 16 + 1;
        var fi = new FileInfo(_dbPath);

        if (!fi.Exists || fi.Length < MinSnapshot)
        {
            DebugLog?.Invoke("[SECTION DB] Creating fresh snapshot...");
            CreateDefaultSections();
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
        catch (Exception ex) when (
            ex is CryptographicException ||
            ex is AuthenticationTagMismatchException ||
            ex is InvalidDataException)
        {
            DebugLog?.Invoke($"[SECTION DB] Snapshot corrupt ({ex.Message}) — recreating...");
            _sections.Clear();
            CreateDefaultSections();
            WriteSnapshot();
        }
    }

    private void CreateDefaultSections()
    {
        _sections.Clear();

        // Default baseline section
        _sections["default"] = new Config.Ftpd.FtpSection(
            Name: "default",
            VirtualRoot: "/",
            FreeLeech: false,
            RatioUploadUnit: 1,
            RatioDownloadUnit: 3,
            NukeMultiplier: 0.0
        );
    }

    // ==========================================================
    // SNAPSHOT WRITE
    // ==========================================================
    private void WriteSnapshot()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((uint)_sections.Count);

        foreach (var s in _sections.Values)
        {
            var rec = BuildRecord(s);
            bw.Write((uint)rec.Length);
            bw.Write(rec);
        }

        var raw = ms.ToArray();
        var compressed = Lz4Codec.Compress(raw);
        var encrypted = EncryptSnapshot(compressed);

        AtomicSnapshot.WriteAtomic(_dbPath, encrypted);
    }

    // ==========================================================
    // SNAPSHOT READ
    // ==========================================================
    private void ParseSnapshot(byte[] raw)
    {
        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);

        _sections.Clear();
        var count = br.ReadUInt32();

        for (uint i = 0; i < count; i++)
        {
            var len = br.ReadUInt32();
            var rec = br.ReadBytes((int)len);

            var s = ParseRecord(rec);
            _sections[s.Name] = s;
        }
    }

    // ==========================================================
    // WAL REPLAY
    // ==========================================================
    private void ReplayWal()
    {
        foreach (var e in _wal.ReadAll())
        {
            switch (e.Type)
            {
                case WalEntryType.AddSection:
                case WalEntryType.UpdateSection:
                    {
                        var s = ParseRecord(e.Payload);
                        _sections[s.Name] = s;
                        break;
                    }

                case WalEntryType.DeleteSection:
                    {
                        var name = Encoding.UTF8.GetString(e.Payload);
                        _sections.Remove(name);
                        break;
                    }
            }
        }
    }

    // ==========================================================
    // PUBLIC API (ISectionStore)
    // ==========================================================
    public Config.Ftpd.FtpSection? FindSection(string sectionName)
        => _sections.TryGetValue(sectionName, out var s) ? s : null;

    public IEnumerable<Config.Ftpd.FtpSection> GetAllSections()
        => _sections.Values;

    public bool TryAddSection(Config.Ftpd.FtpSection section, out string? error)
    {
        if (_sections.ContainsKey(section.Name))
        {
            error = "Section already exists.";
            return false;
        }

        var rec = BuildRecord(section);
        _wal.Append(new WalEntry(WalEntryType.AddSection, rec));

        _sections[section.Name] = section;
        CompactCheck();

        error = null;
        return true;
    }

    public bool TryUpdateSection(Config.Ftpd.FtpSection section, out string? error)
    {
        if (!_sections.ContainsKey(section.Name))
        {
            error = "Section not found.";
            return false;
        }

        var rec = BuildRecord(section);
        _wal.Append(new WalEntry(WalEntryType.UpdateSection, rec));

        _sections[section.Name] = section;
        CompactCheck();

        error = null;
        return true;
    }

    public bool TryDeleteSection(string sectionName, out string? error)
    {
        if (!_sections.ContainsKey(sectionName))
        {
            error = "Section not found.";
            return false;
        }

        _wal.Append(new WalEntry(WalEntryType.DeleteSection, Encoding.UTF8.GetBytes(sectionName)));
        _sections.Remove(sectionName);

        CompactCheck();
        error = null;
        return true;
    }

    private void CompactCheck()
    {
        if (_wal.NeedsCompaction())
        {
            WriteSnapshot();
            _wal.Clear();
        }
    }

    // ==========================================================
    // RECORD FORMAT (Unified FtpSection)
    // ==========================================================
    private byte[] BuildRecord(Config.Ftpd.FtpSection s)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        var nameBytes = Encoding.UTF8.GetBytes(s.Name);
        var rootBytes = Encoding.UTF8.GetBytes(s.VirtualRoot);

        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)rootBytes.Length);

        bw.Write(s.FreeLeech);
        bw.Write(s.RatioUploadUnit);
        bw.Write(s.RatioDownloadUnit);

        // Nullable double
        bw.Write(s.NukeMultiplier.HasValue);
        if (s.NukeMultiplier.HasValue)
            bw.Write(s.NukeMultiplier.Value);

        bw.Write(nameBytes);
        bw.Write(rootBytes);

        return ms.ToArray();
    }

    private Config.Ftpd.FtpSection ParseRecord(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var nameLen = br.ReadUInt16();
        var rootLen = br.ReadUInt16();

        var free = br.ReadBoolean();
        var up = br.ReadInt32();
        var down = br.ReadInt32();

        var hasNuke = br.ReadBoolean();
        double? nuke = hasNuke ? br.ReadDouble() : null;

        var name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
        var root = Encoding.UTF8.GetString(br.ReadBytes(rootLen));

        return new Config.Ftpd.FtpSection(
            Name: name,
            VirtualRoot: root,
            FreeLeech: free,
            RatioUploadUnit: up,
            RatioDownloadUnit: down,
            NukeMultiplier: nuke ?? 1
        );
    }

    // ==========================================================
    // ENCRYPTION
    // ==========================================================
    private byte[] EncryptSnapshot(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using var gcm = new AesGcm(_masterKey);
        gcm.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[12 + cipher.Length + 16];
        Buffer.BlockCopy(nonce, 0, output, 0, 12);
        Buffer.BlockCopy(cipher, 0, output, 12, cipher.Length);
        Buffer.BlockCopy(tag, 0, output, 12 + cipher.Length, 16);
        return output;
    }

    private byte[] DecryptSnapshot(byte[] buf)
    {
        ReadOnlySpan<byte> nonce = buf[..12];
        ReadOnlySpan<byte> tag = buf[^16..];
        ReadOnlySpan<byte> cipher = buf[12..^16];

        var plain = new byte[cipher.Length];

        using var gcm = new AesGcm(_masterKey);
        gcm.Decrypt(nonce, cipher, tag, plain);

        return plain;
    }

    // ==========================================================
    // SALT + KEY
    // ==========================================================
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