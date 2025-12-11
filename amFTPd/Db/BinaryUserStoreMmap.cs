/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           BinaryUserStoreMmap.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 20:12:17
 *  Last Modified:  2025-12-11 08:13:19
 *  CRC32:          0xD8BD3573
 *  
 *  Description:
 *      Represents a memory-mapped binary user store that provides secure and efficient storage and retrieval of FTP user dat...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */









using amFTPd.Config.Ftpd;
using amFTPd.Security;
using amFTPd.Utils;
using System.Collections.Immutable;
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

        private readonly WalFile _wal;

        public Action<string>? DebugLog;

        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryUserStoreMmap"/> class, creating or loading a binary user
        /// store from the specified database path.
        /// </summary>
        /// <remarks>This constructor initializes the database by ensuring the necessary metadata files
        /// exist, deriving the encryption key from the provided master password, and replaying the write-ahead log
        /// (WAL) to ensure the database is in a consistent state. If the database does not exist, it will be
        /// created.</remarks>
        /// <param name="dbPath">The file path to the database. This path is used to locate or create the database file and associated
        /// metadata files.</param>
        /// <param name="masterPassword">The master password used to derive the encryption key for securing the database.</param>
        public BinaryUserStoreMmap(string dbPath, string masterPassword)
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
        // FIRST RUN / LOAD / CORRUPTION RECOVERY
        // ==========================================================

        private void LoadOrCreateSnapshot()
        {
            const int MinSnapshotSize = 12 + 16 + 1; // nonce + tag + 1 byte

            // new FileInfo(dbPath) is allowed even if file missing
            var fi = new FileInfo(_dbPath);

            if (!fi.Exists || fi.Length < MinSnapshotSize)
            {
                DebugLog?.Invoke("[USER DB] Snapshot missing or too small — creating new DB");
                _users.Clear();
                CreateBootstrapAdmin();
                WriteSnapshot();
                return;
            }

            try
            {
                DebugLog?.Invoke("[USER DB] Loading encrypted snapshot...");
                var encrypted = File.ReadAllBytes(_dbPath);
                var decrypted = DecryptSnapshot(encrypted);
                var raw = Lz4Codec.Decompress(decrypted);
                ParseUserSnapshot(raw);
            }
            catch (Exception ex) when (
                ex is CryptographicException ||
                ex is AuthenticationTagMismatchException ||
                ex is InvalidDataException)
            {
                DebugLog?.Invoke("[USER DB] Snapshot corrupt — recreating fresh DB.");
                _users.Clear();
                CreateBootstrapAdmin();
                WriteSnapshot();
            }
        }

        private void CreateBootstrapAdmin()
        {
            var admin = new FtpUser(
                UserName: "admin",
                PasswordHash: PasswordHasher.HashPassword("admin"),
                HomeDir: "/",
                IsAdmin: true,
                AllowFxp: true,
                AllowUpload: true,
                AllowDownload: true,
                AllowActiveMode: true,
                MaxConcurrentLogins: 0,
                IdleTimeout: TimeSpan.FromHours(24),
                MaxUploadKbps: 0,
                MaxDownloadKbps: 0,
                PrimaryGroup: "admins",
                SecondaryGroups: ImmutableArray<string>.Empty,
                CreditsKb: long.MaxValue,
                AllowedIpMask: null,
                RequireIdentMatch: false,
                RequiredIdent: null,
                FlagsRaw: "MS1ZR"
            );

            _users[admin.UserName] = admin;
        }

        // ==========================================================
        // SNAPSHOT SAVE
        // ==========================================================

        private void WriteSnapshot()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((uint)_users.Count);

            foreach (var u in _users.Values)
            {
                var rec = BuildRecord(u);
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

                // record type, always 0
                var type = rbr.ReadByte();
                if (type != 0)
                    continue;

                var user = ParseRecordFromReader(rbr);
                _users[user.UserName] = user;
            }
        }

        // ==========================================================
        // WAL REPLAY
        // ==========================================================

        private void ReplayWal()
        {
            DebugLog?.Invoke("[USER DB] Replaying WAL…");

            foreach (var e in _wal.ReadAll())
            {
                switch (e.Type)
                {
                    case WalEntryType.AddUser:
                    case WalEntryType.UpdateUser:
                        {
                            var u = ParseRecord(e.Payload);
                            _users[u.UserName] = u;
                            break;
                        }
                    case WalEntryType.DeleteUser:
                        {
                            var name = Encoding.UTF8.GetString(e.Payload);
                            _users.Remove(name);
                            break;
                        }
                }
            }
        }

        // ==========================================================
        // PUBLIC API
        // ==========================================================

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
                var c = _loginCounter.TryGetValue(user, out var cur) ? cur : 0;
                if (u.MaxConcurrentLogins > 0 && c >= u.MaxConcurrentLogins)
                    return false;

                _loginCounter[user] = c + 1;
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

        // ==========================================================
        // MUTATION API
        // ==========================================================

        public bool TryAddUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (_users.ContainsKey(user.UserName))
                {
                    error = "User exists";
                    return false;
                }

                var record = BuildRecord(user);
                _wal.Append(new WalEntry(WalEntryType.AddUser, record));
                _users[user.UserName] = user;

                error = null;
                WriteSnapshot();
                return true;
            }
        }

        public bool TryUpdateUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (!_users.ContainsKey(user.UserName))
                {
                    error = "User not found";
                    return false;
                }

                var record = BuildRecord(user);
                _wal.Append(new WalEntry(WalEntryType.UpdateUser, record));
                _users[user.UserName] = user;

                error = null;
                WriteSnapshot();
                return true;
            }
        }

        // ==========================================================
        // RECORD FORMAT
        // (unchanged from your current implementation)
        // ==========================================================

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
            bw.Write((int)(u.IdleTimeout ?? TimeSpan.Zero).TotalSeconds);
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
                FlagsRaw: ""
            );
        }

        // ==========================================================
        // ENCRYPTION
        // ==========================================================

        private byte[] DecryptSnapshot(byte[] buf)
        {
            ReadOnlySpan<byte> nonce = buf[..12];
            ReadOnlySpan<byte> tag = buf[^16..];
            ReadOnlySpan<byte> cipher = buf[12..^16];

            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(_masterKey, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);

            return plain;
        }

        private byte[] EncryptSnapshot(byte[] plain)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];

            using var gcm = new AesGcm(_masterKey, 16);
            gcm.Encrypt(nonce, plain, cipher, tag);

            var result = new byte[nonce.Length + cipher.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(cipher, 0, result, nonce.Length, cipher.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length + cipher.Length, tag.Length);
            return result;
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
