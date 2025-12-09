/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-26
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
using System.Security.Cryptography;
using System.Text;

namespace amFTPd.Db
{
    /// <summary>
    /// Provides a binary-based implementation of the <see cref="IUserStore"/> interface for managing FTP users.
    /// </summary>
    /// <remarks>This class stores user data in a binary file format, with encryption for secure storage. It
    /// supports operations such as user authentication, adding, updating, and retrieving users, as well as managing
    /// concurrent login limits. The database is loaded from the specified file path during initialization and saved
    /// back to the file upon modifications. <para> The class is thread-safe, ensuring proper synchronization for
    /// concurrent access to user data. </para></remarks>
    public sealed class BinaryUserStore : IUserStore
    {
        private readonly string _dbPath;
        private readonly string _walPath;

        private readonly byte[] _masterKey;
        private readonly byte[] _masterPasswordBytes;

        private readonly Dictionary<string, FtpUser> _users
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _loginCounter
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly Lock _sync = new();

        private readonly WalFile _wal;
        private FileSystemWatcher? _watcher;

        public Action<string>? DebugLog;

        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryUserStore"/> class, which provides secure storage for
        /// user data using a binary file format with write-ahead logging (WAL).
        /// </summary>
        /// <remarks>This constructor initializes the database by ensuring the existence of a salt file,
        /// deriving the encryption key from the provided master password, and setting up the write-ahead log. If the
        /// database file does not exist, a new one is created. The constructor also replays any existing WAL entries to
        /// ensure the database is up-to-date and starts a file watcher to monitor changes. <para> The write-ahead log
        /// file size is limited to 5 MB by default. </para></remarks>
        /// <param name="dbPath">The file path to the database file. This path is used to store the main database, the write-ahead log file,
        /// and the salt file for key derivation.</param>
        /// <param name="masterPassword">The master password used to derive the encryption key for securing the database.</param>
        public BinaryUserStore(string dbPath, string masterPassword)
        {
            _dbPath = dbPath;
            _walPath = dbPath + ".wal";
            _masterPasswordBytes = Encoding.UTF8.GetBytes(masterPassword);

            // Derive master key from password + salt file
            var salt = EnsureSalt(dbPath + ".salt");
            _masterKey = DeriveKey(salt);

            _wal = new WalFile(_walPath, _masterKey)
            {
                MaxWalSizeBytes = 5 * 1024 * 1024
            };

            LoadOrCreateSnapshot();
            ReplayWal();

            StartWatcher();
        }

        // ========================================================
        // INITIAL LOAD / FIRST RUN HANDLING
        // ========================================================
        private void LoadOrCreateSnapshot()
        {
            const int MinSnapshotSize = 12 + 16 + 1;

            var fi = new FileInfo(_dbPath);

            if (!fi.Exists || fi.Length < MinSnapshotSize)
            {
                DebugLog?.Invoke("[USER DB] Creating new snapshot (first run)");
                CreateBootstrapAdmin();
                WriteSnapshot();
                return;
            }

            try
            {
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
                DebugLog?.Invoke($"[USER DB] Snapshot corrupt, recreating: {ex.Message}");
                _users.Clear();
                CreateBootstrapAdmin();
                WriteSnapshot();
            }
        }

        private void CreateBootstrapAdmin()
        {
            _users.Clear();

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

        // ========================================================
        // SNAPSHOT WRITE
        // ========================================================
        private void WriteSnapshot()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((uint)_users.Count);

            foreach (var u in _users.Values)
            {
                var rec = BuildRecordIncludingType(u);
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
        private void ParseUserSnapshot(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var br = new BinaryReader(ms);

            _users.Clear();

            var count = br.ReadUInt32();

            for (uint i = 0; i < count; i++)
            {
                var len = br.ReadUInt32();
                var recBytes = br.ReadBytes((int)len);

                using var rms = new MemoryStream(recBytes);
                using var rbr = new BinaryReader(rms);

                var type = rbr.ReadByte();
                if (type != 0)
                    continue;

                var user = ParseRecordFromReader(rbr);
                _users[user.UserName] = user;
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

        // ========================================================
        // PUBLIC API
        // ========================================================
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

        public bool TryAddUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (_users.ContainsKey(user.UserName))
                {
                    error = "User exists";
                    return false;
                }

                var rec = BuildRecord(user);
                _wal.Append(new WalEntry(WalEntryType.AddUser, rec));
                _users[user.UserName] = user;

                if (_wal.NeedsCompaction())
                    RewriteSnapshotAndClearWal();

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
                    error = "User not found";
                    return false;
                }

                var rec = BuildRecord(user);
                _wal.Append(new WalEntry(WalEntryType.UpdateUser, rec));
                _users[user.UserName] = user;

                if (_wal.NeedsCompaction())
                    RewriteSnapshotAndClearWal();

                error = null;
                return true;
            }
        }

        public bool TryDeleteUser(string userName, out string? error)
        {
            lock (_sync)
            {
                if (!_users.ContainsKey(userName))
                {
                    error = "User not found";
                    return false;
                }

                _wal.Append(new WalEntry(WalEntryType.DeleteUser,
                    Encoding.UTF8.GetBytes(userName)));

                _users.Remove(userName);

                if (_wal.NeedsCompaction())
                    RewriteSnapshotAndClearWal();

                error = null;
                return true;
            }
        }

        public void ForceSnapshotRewrite()
        {
            lock (_sync)
            {
                WriteSnapshot();
                _wal.Clear();
            }
        }

        // ========================================================
        // SNAPSHOT + WAL
        // ========================================================
        private void RewriteSnapshotAndClearWal()
        {
            WriteSnapshot();
            _wal.Clear();
        }

        private Dictionary<string, FtpUser> ParseSnapshotData(BinaryReader br)
        {
            var count = br.ReadUInt32();
            var dict = new Dictionary<string, FtpUser>();

            for (int i = 0; i < count; i++)
            {
                var len = br.ReadUInt32();
                var rec = br.ReadBytes((int)len);

                using var rms = new MemoryStream(rec);
                using var rbr = new BinaryReader(rms);

                var type = rbr.ReadByte();
                if (type != 0)
                    continue;

                var user = ParseRecordFromReader(rbr);
                dict[user.UserName] = user;
            }

            return dict;
        }


        // ========================================================
        // RECORD FORMAT (same as Mmap version)
        // ========================================================
        private byte[] BuildRecord(FtpUser u)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            var nameLen = (ushort)Encoding.UTF8.GetByteCount(u.UserName);
            var passLen = (ushort)Encoding.UTF8.GetByteCount(u.PasswordHash);
            var homeLen = (ushort)Encoding.UTF8.GetByteCount(u.HomeDir);
            var groupLen = (ushort)(u.GroupName == null ? 0 : Encoding.UTF8.GetByteCount(u.GroupName));

            var ip = u.AllowedIpMask == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.AllowedIpMask);
            var ident = u.RequiredIdent == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.RequiredIdent);

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

            bw.Write((ushort)ip.Length);
            bw.Write((ushort)ident.Length);

            bw.Write(Encoding.UTF8.GetBytes(u.UserName));
            bw.Write(Encoding.UTF8.GetBytes(u.PasswordHash));
            bw.Write(Encoding.UTF8.GetBytes(u.HomeDir));

            if (groupLen > 0) bw.Write(Encoding.UTF8.GetBytes(u.GroupName!));
            if (ip.Length > 0) bw.Write(ip);
            if (ident.Length > 0) bw.Write(ident);

            return ms.ToArray();
        }

        private byte[] BuildRecordIncludingType(FtpUser u)
        {
            var rec = BuildRecord(u);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((byte)0); // record type
            bw.Write(rec);

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

            var ipLen = br.ReadUInt16();
            var identLen = br.ReadUInt16();

            var user = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
            var pass = Encoding.UTF8.GetString(br.ReadBytes(passLen));
            var home = Encoding.UTF8.GetString(br.ReadBytes(homeLen));

            var group = groupLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(groupLen)) : null;
            var ip = ipLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(ipLen)) : null;
            var ident = identLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(identLen)) : null;

            return new FtpUser(
                UserName: user,
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

        // ========================================================
        // ENCRYPTION HELPERS
        // ========================================================
        private byte[] EncryptSnapshot(byte[] plain)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];

            using var gcm = new AesGcm(_masterKey);
            gcm.Encrypt(nonce, plain, cipher, tag);

            var result = new byte[nonce.Length + cipher.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(cipher, 0, result, nonce.Length, cipher.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length + cipher.Length, tag.Length);

            return result;
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

        private byte[] DeriveKey(byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                _masterPasswordBytes,
                salt,
                200_000,
                HashAlgorithmName.SHA256);

            return pbkdf2.GetBytes(32);
        }

        private static byte[] EnsureSalt(string saltPath)
        {
            if (File.Exists(saltPath))
                return File.ReadAllBytes(saltPath);

            var salt = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(saltPath, salt);
            return salt;
        }

        // ========================================================
        // HOT RELOAD SUPPORT
        // ========================================================
        private void StartWatcher()
        {
            var dir = Path.GetDirectoryName(_dbPath)!;
            var file = Path.GetFileName(_dbPath);

            _watcher = new FileSystemWatcher(dir, file);

            _watcher.NotifyFilter = NotifyFilters.LastWrite;
            _watcher.Changed += (_, __) =>
            {
                lock (_sync)
                {
                    try
                    {
                        DebugLog?.Invoke("[USER DB] Hot reload triggered...");
                        var encrypted = File.ReadAllBytes(_dbPath);
                        var dec = DecryptSnapshot(encrypted);
                        var raw = Lz4Codec.Decompress(dec);

                        using var ms = new MemoryStream(raw);
                        using var br = new BinaryReader(ms);

                        var loaded = ParseSnapshotData(br);
                        foreach (var kv in loaded)
                            _users[kv.Key] = kv.Value;
                    }
                    catch
                    {
                        DebugLog?.Invoke("[USER DB] Hot reload failed (corruption?)");
                    }
                }
            };

            _watcher.EnableRaisingEvents = true;
        }
    }
}
