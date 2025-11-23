/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-23
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

using amFTPd.Config.Ftpd;
using amFTPd.Security;
using amFTPd.Utils;
using System.Collections.Immutable;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;

namespace amFTPd.Db
{
    /// <summary>
    /// Represents a memory-mapped binary user store that provides secure and efficient storage and retrieval of FTP
    /// user data. This implementation supports concurrent access, user authentication, and user management operations.
    /// </summary>
    /// <remarks>The <see cref="BinaryUserStoreMmap"/> class uses a memory-mapped file to load and manage user
    /// data snapshots, ensuring fast access to user records. Write-ahead logging (WAL) is employed to maintain data
    /// consistency and durability. This class is thread-safe for concurrent read and write operations. <para> The user
    /// data is encrypted and compressed for security and storage efficiency. Changes to the user store are logged in
    /// the WAL file and applied to the in-memory snapshot. </para> <para> This class implements the <see
    /// cref="IUserStore"/> interface, providing methods for user authentication, retrieval, and management.
    /// </para></remarks>
    public sealed class BinaryUserStoreMmap : IUserStore
    {
        private readonly string _dbPath;
        private readonly string _walPath;
        private readonly byte[] _masterKey;

        private readonly Dictionary<string, FtpUser> _users
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _loginCounter
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly Lock _sync = new();

        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _acc;

        private readonly WalFile _wal;

        public Action<string>? DebugLog;

        public BinaryUserStoreMmap(string dbPath, string masterPassword)
        {
            _dbPath = dbPath;
            _walPath = dbPath + ".wal";

            var salt = EnsureSalt(dbPath + ".salt");
            _masterKey = DeriveKey(masterPassword, salt);

            _wal = new WalFile(_walPath, _masterKey);

            // If DB doesn't exist, create one from full BinaryUserStore
            if (!File.Exists(dbPath))
            {
                var tmp = new BinaryUserStore(dbPath, masterPassword);
                // That constructor already writes snapshot
            }

            MapSnapshot();

            ReplayWal();
        }

        // ======================================================================
        // MMAP LOADING
        // ======================================================================

        private void MapSnapshot()
        {
            DebugLog?.Invoke("[MMAP] Mapping snapshot...");

            // Dispose old mmaps
            _acc?.Dispose();
            _mmf?.Dispose();

            _mmf = MemoryMappedFile.CreateFromFile(
                _dbPath,
                FileMode.Open,
                null,
                0,
                MemoryMappedFileAccess.Read);

            _acc = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            // Read entire mapped memory into byte[]
            var length = _acc.Capacity;
            var buf = new byte[length];
            _acc.ReadArray(0, buf, 0, (int)length);

            // Decrypt
            var decrypted = DecryptSnapshot(buf);

            // Decompress
            var raw = Lz4Codec.Decompress(decrypted);

            // Parse v1 snapshot format
            ParseUserSnapshot(raw);
        }

        private void ParseUserSnapshot(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var br = new BinaryReader(ms);

            _users.Clear();

            var count = br.ReadUInt32();

            for (uint i = 0; i < count; i++)
            {
                var len = br.ReadUInt32();
                var rec = br.ReadBytes((int)len);

                using var rms = new MemoryStream(rec);
                using var rbr = new BinaryReader(rms);

                var type = rbr.ReadByte();
                if (type != 0)
                    continue;

                var u = ParseRecordFromReader(rbr);
                _users[u.UserName] = u;
            }
        }

        // ======================================================================
        // WAL REPLAY
        // ======================================================================

        private void ReplayWal()
        {
            DebugLog?.Invoke("[MMAP WAL] Replaying WAL...");

            foreach (var e in _wal.ReadAll())
            {
                switch (e.Type)
                {
                    case WalEntryType.AddUser:
                        {
                            var u = ParseRecord(e.Payload);
                            DebugLog?.Invoke($"[MMAP WAL] AddUser {u.UserName}");
                            _users[u.UserName] = u;
                            break;
                        }

                    case WalEntryType.UpdateUser:
                        {
                            var u = ParseRecord(e.Payload);
                            DebugLog?.Invoke($"[MMAP WAL] UpdateUser {u.UserName}");
                            _users[u.UserName] = u;
                            break;
                        }

                    case WalEntryType.DeleteUser:
                        {
                            var name = Encoding.UTF8.GetString(e.Payload);
                            DebugLog?.Invoke($"[MMAP WAL] DeleteUser {name}");
                            _users.Remove(name);
                            break;
                        }
                }
            }
        }

        // ======================================================================
        // PUBLIC API
        // ======================================================================

        public FtpUser? FindUser(string userName)
            => _users.TryGetValue(userName, out var u) ? u : null;

        public IEnumerable<FtpUser> GetAllUsers()
            => _users.Values;

        public bool TryAuthenticate(string user, string password, out FtpUser? account)
        {
            account = null;

            if (!_users.TryGetValue(user, out var u))
                return false;

            if (!PasswordHasher.VerifyPassword(password, u.PasswordHash))
                return false;

            lock (_sync)
            {
                var active = _loginCounter.TryGetValue(user, out var c) ? c : 0;
                if (u.MaxConcurrentLogins > 0 && active >= u.MaxConcurrentLogins)
                    return false;

                _loginCounter[user] = active + 1;
            }

            account = u;
            return true;
        }

        public void OnLogout(FtpUser user)
        {
            lock (_sync)
            {
                if (_loginCounter.TryGetValue(user.UserName, out var c) && c > 0)
                    _loginCounter[user.UserName] = c - 1;
            }
        }

        // ======================================================================
        // MUTATION API (required by IUserStore)
        // ======================================================================

        public bool TryAddUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (_users.ContainsKey(user.UserName))
                {
                    error = "User exists.";
                    return false;
                }

                // Build WAL record (same as BinaryUserStore)
                var record = BuildRecord(user);
                _wal.Append(new WalEntry(WalEntryType.AddUser, record));

                // Update in-memory (overlays MMAP)
                _users[user.UserName] = user;

                DebugLog?.Invoke($"[MMAP WAL] Added user '{user.UserName}'");

                error = null;
                return true;
            }
        }

        public bool TryUpdateUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (!_users.ContainsKey(user.UserName))
                {
                    error = "User not found.";
                    return false;
                }

                var record = BuildRecord(user);
                _wal.Append(new WalEntry(WalEntryType.UpdateUser, record));

                _users[user.UserName] = user;

                DebugLog?.Invoke($"[MMAP WAL] Updated user '{user.UserName}'");

                error = null;
                return true;
            }
        }


        // ======================================================================
        // RECORD FORMAT (same as v3 UserStore)
        // ======================================================================

        private byte[] BuildRecord(FtpUser u)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            var nameLen = (ushort)Encoding.UTF8.GetByteCount(u.UserName);
            var passLen = (ushort)Encoding.UTF8.GetByteCount(u.PasswordHash);
            var homeLen = (ushort)Encoding.UTF8.GetByteCount(u.HomeDir);
            var groupLen = (ushort)(u.GroupName == null ? 0 : Encoding.UTF8.GetByteCount(u.GroupName));

            var ipBytes = u.AllowedIpMask == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.AllowedIpMask);
            var identBytes = u.RequiredIdent == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.RequiredIdent);

            bw.Write(nameLen);
            bw.Write(passLen);
            bw.Write(homeLen);
            bw.Write(groupLen);

            var flags =
                (u.IsAdmin ? 1 : 0) |
                (u.AllowFxp ? 2 : 0) |
                (u.AllowUpload ? 4 : 0) |
                (u.AllowDownload ? 8 : 0) |
                (u.AllowActiveMode ? 16 : 0) |
                (u.RequireIdentMatch ? 32 : 0);

            bw.Write(flags);
            bw.Write(u.MaxConcurrentLogins);
            bw.Write((int)u.IdleTimeout.TotalSeconds);
            bw.Write(u.MaxUploadKbps);
            bw.Write(u.MaxDownloadKbps);
            bw.Write(u.CreditsKb);

            bw.Write((ushort)ipBytes.Length);
            bw.Write((ushort)identBytes.Length);

            bw.Write(Encoding.UTF8.GetBytes(u.UserName));
            bw.Write(Encoding.UTF8.GetBytes(u.PasswordHash));
            bw.Write(Encoding.UTF8.GetBytes(u.HomeDir));

            if (groupLen > 0)
                bw.Write(Encoding.UTF8.GetBytes(u.GroupName!));

            if (ipBytes.Length > 0)
                bw.Write(ipBytes);

            if (identBytes.Length > 0)
                bw.Write(identBytes);

            return ms.ToArray();
        }

        private FtpUser ParseRecord(byte[] buf)
        {
            using var ms = new MemoryStream(buf);
            using var br = new BinaryReader(ms);
            return ParseRecordFromReader(br);
        }

        private FtpUser ParseRecordFromReader(BinaryReader br)
        {
            var nameLen = br.ReadUInt16();
            var passLen = br.ReadUInt16();
            var homeLen = br.ReadUInt16();
            var groupLen = br.ReadUInt16();

            var flags = br.ReadInt32();

            var maxLogins = br.ReadInt32();
            var idleSec = br.ReadInt32();
            var up = br.ReadInt32();
            var down = br.ReadInt32();
            var credits = br.ReadInt64();

            var ipMaskLen = br.ReadUInt16();
            var identLen = br.ReadUInt16();

            var name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
            var pass = Encoding.UTF8.GetString(br.ReadBytes(passLen));
            var home = Encoding.UTF8.GetString(br.ReadBytes(homeLen));

            var group = groupLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(groupLen)) : null;
            var ip = ipMaskLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(ipMaskLen)) : null;
            var ident = identLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(identLen)) : null;

            return new FtpUser(
                UserName: name,
                PasswordHash: pass,
                HomeDir: home,
                IsAdmin: (flags & 1) != 0,
                AllowFxp: (flags & 2) != 0,
                AllowUpload: (flags & 4) != 0,
                AllowDownload: (flags & 8) != 0,
                AllowActiveMode: (flags & 16) != 0,
                MaxConcurrentLogins: maxLogins,
                IdleTimeout: TimeSpan.FromSeconds(idleSec),
                MaxUploadKbps: up,
                MaxDownloadKbps: down,
                PrimaryGroup: group,
                SecondaryGroups: ImmutableArray<string>.Empty,
                CreditsKb: credits,
                AllowedIpMask: ip,
                RequireIdentMatch: (flags & 32) != 0,
                RequiredIdent: ident,
                FlagsRaw: string.Empty
            );

        }

        // ======================================================================
        // ENCRYPTION HELPERS
        // ======================================================================

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
    }
}
