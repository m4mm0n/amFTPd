using amFTPd.Config.Ftpd;
using amFTPd.Db;
using amFTPd.Security;
using System.Security.Cryptography;
using System.Text;

namespace amFTPd.Utils
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
        private readonly byte[] _masterPasswordBytes;

        private readonly Dictionary<string, FtpUser> _users;
        private readonly Dictionary<string, int> _loginCounter = new();
        private readonly Lock _sync = new();

        private readonly WalFile _wal;
        private FileSystemWatcher? _watcher;

        /// <summary>
        /// Represents a hookable debug logger that can be used to log debug messages.
        /// </summary>
        /// <remarks>Assign a method to this delegate to handle debug log messages. The assigned method
        /// should accept a single string parameter representing the debug message to log. If no method is assigned,
        /// debug messages will not be logged.</remarks>
        public Action<string>? DebugLog; // hookable debug logger
        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryUserStore"/> class, which provides a secure, binary-based
        /// user storage system with write-ahead logging (WAL) for durability.
        /// </summary>
        /// <remarks>This constructor initializes the database by either loading an existing snapshot or
        /// creating a new empty database. It also replays the write-ahead log (WAL) to ensure the database is in a
        /// consistent state. The master password is used to derive a cryptographic key, which is combined with a
        /// per-file salt for enhanced security.  The WAL file is automatically managed and has a default maximum size
        /// of 5 MB. A file watcher is started to monitor changes to the database file.</remarks>
        /// <param name="dbPath">The file path to the database file. If the file does not exist, a new database will be created.</param>
        /// <param name="masterPassword">The master password used to derive the encryption key for securing the database.</param>
        public BinaryUserStore(string dbPath, string masterPassword)
        {
            _dbPath = dbPath;
            _walPath = dbPath + ".wal";
            _masterPasswordBytes = Encoding.UTF8.GetBytes(masterPassword);

            // Master key derived from password and per-file salt
            var keySalt = EnsureSaltFile(dbPath + ".salt");
            _masterKey = DeriveKey(keySalt);

            _wal = new WalFile(_walPath, _masterKey)
            {
                MaxWalSizeBytes = 5 * 1024 * 1024 // 5 MB default
            };

            // load DB snapshot
            if (File.Exists(dbPath))
                _users = LoadDb();
            else
                _users = CreateEmptyDb();

            // Replay WAL after snapshot
            ReplayWal();

            StartWatcher();
        }

        // Derived AES-GCM key (32 bytes)
        private readonly byte[] _masterKey;

        // ========================================================================
        // PUBLIC INTERFACE
        // ========================================================================
        /// <summary>
        /// Attempts to authenticate a user with the provided credentials.
        /// </summary>
        /// <remarks>This method verifies the provided username and password against the stored user data.
        /// If the user is authenticated, it also ensures that the number of concurrent logins does not exceed the
        /// maximum allowed for the user.</remarks>
        /// <param name="user">The username to authenticate.</param>
        /// <param name="password">The password associated with the specified username.</param>
        /// <param name="account">When this method returns, contains the authenticated <see cref="FtpUser"/> object if authentication
        /// succeeds; otherwise, <see langword="null"/>. This parameter is passed uninitialized.</param>
        /// <returns><see langword="true"/> if the authentication is successful; otherwise, <see langword="false"/>.</returns>
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
        /// <summary>
        /// Handles the logout process for the specified FTP user, decrementing their active login count.
        /// </summary>
        /// <remarks>This method ensures that the active login count for the user is decremented in a
        /// thread-safe manner. If the user does not have an active login count, no changes are made.</remarks>
        /// <param name="user">The FTP user who is logging out. The <see cref="FtpUser.UserName"/> property must not be null.</param>
        public void OnLogout(FtpUser user)
        {
            lock (_sync)
            {
                if (_loginCounter.TryGetValue(user.UserName, out var c) && c > 0)
                    _loginCounter[user.UserName] = c - 1;
            }
        }
        /// <summary>
        /// Finds and returns the user associated with the specified username.
        /// </summary>
        /// <param name="userName">The username of the user to find. Cannot be <see langword="null"/> or empty.</param>
        /// <returns>The <see cref="FtpUser"/> object associated with the specified username, or <see langword="null"/> if no
        /// user is found.</returns>
        public FtpUser? FindUser(string userName)
            => _users.TryGetValue(userName, out var u) ? u : null;
        /// <summary>
        /// Retrieves all users currently stored in the system.
        /// </summary>
        /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="FtpUser"/> objects representing all users.  The collection
        /// will be empty if no users are available.</returns>
        public IEnumerable<FtpUser> GetAllUsers()
            => _users.Values;
        /// <summary>
        /// Attempts to add a new FTP user to the system.
        /// </summary>
        /// <remarks>This method ensures thread safety by locking during the operation. If a user with the
        /// same username already exists, the method returns <see langword="false"/> and sets the <paramref
        /// name="error"/> parameter to an appropriate error message. If the operation succeeds, the user is added, and
        /// the write-ahead log (WAL) is updated. The method may trigger a snapshot rewrite if the WAL requires
        /// compaction.</remarks>
        /// <param name="user">The <see cref="FtpUser"/> object representing the user to be added.</param>
        /// <param name="error">When this method returns, contains an error message if the operation fails; otherwise, <see
        /// langword="null"/>.</param>
        /// <returns><see langword="true"/> if the user was successfully added; otherwise, <see langword="false"/>.</returns>
        public bool TryAddUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (_users.ContainsKey(user.UserName))
                {
                    error = "User exists.";
                    return false;
                }

                var recordBytes = BuildRecord(user);
                _wal.Append(new WalEntry(WalEntryType.AddUser, recordBytes));
                _users[user.UserName] = user;

                if (_wal.NeedsCompaction())
                    RewriteSnapshotAndClearWal();

                error = null;
                return true;
            }
        }
        /// <summary>
        /// Attempts to update the specified user in the system.
        /// </summary>
        /// <remarks>This method is thread-safe. If the specified user does not exist in the system, the
        /// method returns <see langword="false"/> and sets the <paramref name="error"/> parameter to an appropriate
        /// error message.</remarks>
        /// <param name="user">The <see cref="FtpUser"/> object containing the updated user information. The user's username must already
        /// exist in the system.</param>
        /// <param name="error">When this method returns, contains an error message if the update operation fails; otherwise, <see
        /// langword="null"/>. This parameter is passed uninitialized.</param>
        /// <returns><see langword="true"/> if the user was successfully updated; otherwise, <see langword="false"/>.</returns>
        public bool TryUpdateUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (!_users.ContainsKey(user.UserName))
                {
                    error = "Not found.";
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
        /// <summary>
        /// Attempts to delete a user by their username.
        /// </summary>
        /// <remarks>This method is thread-safe. If the specified user does not exist, the method returns
        /// <see langword="false"/> and sets the <paramref name="error"/> parameter to an appropriate error
        /// message.</remarks>
        /// <param name="userName">The name of the user to delete. This value cannot be <see langword="null"/> or empty.</param>
        /// <param name="error">When this method returns, contains an error message if the operation fails; otherwise, <see
        /// langword="null"/>. This parameter is passed uninitialized.</param>
        /// <returns><see langword="true"/> if the user was successfully deleted; otherwise, <see langword="false"/>.</returns>
        public bool TryDeleteUser(string userName, out string? error)
        {
            lock (_sync)
            {
                if (!_users.ContainsKey(userName))
                {
                    error = "Not found.";
                    error = null;
                    return false;
                }

                var rec = Encoding.UTF8.GetBytes(userName);
                _wal.Append(new WalEntry(WalEntryType.DeleteUser, rec));

                _users.Remove(userName);

                if (_wal.NeedsCompaction())
                    RewriteSnapshotAndClearWal();

                error = null;
                return true;
            }
        }

        // ========================================================================
        // CREATE EMPTY DB (with default admin user)
        // ========================================================================
        private Dictionary<string, FtpUser> CreateEmptyDb()
        {
            DebugLog?.Invoke("[DB] Creating new empty database with default admin user...");

            var dict = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);

            // Default admin user (matches classic FTP daemon behavior)
            var admin = new FtpUser(
                UserName: "admin",
                PasswordHash: PasswordHasher.HashPassword("admin"),
                HomeDir: "/",
                IsAdmin: true,
                AllowFxp: false,
                AllowUpload: true,
                AllowDownload: true,
                AllowActiveMode: true,
                MaxConcurrentLogins: 5,
                IdleTimeout: TimeSpan.FromMinutes(30),
                MaxUploadKbps: 0,
                MaxDownloadKbps: 0,
                GroupName: "admins",
                CreditsKb: 1024 * 1024, // 1GB default credits
                AllowedIpMask: null,
                RequireIdentMatch: false,
                RequiredIdent: null
            );

            dict["admin"] = admin;

            // Write the initial snapshot to disk
            var snapshot = BuildSnapshotBytes(dict);
            AtomicSnapshot.WriteAtomic(_dbPath, snapshot);

            // Clear any old WAL (shouldn't exist, but just in case)
            _wal.Clear();

            DebugLog?.Invoke("[DB] Empty DB created and snapshot written.");

            return dict;
        }

        // ========================================================================
        // WAL REPLAY (DEBUG ENABLED)
        // ========================================================================
        private void ReplayWal()
        {
            foreach (var entry in _wal.ReadAll())
            {
                switch (entry.Type)
                {
                    case WalEntryType.AddUser:
                        {
                            var user = ParseRecord(entry.Payload);
                            DebugLog?.Invoke($"[WAL] Replay AddUser: {user.UserName}");
                            _users[user.UserName] = user;
                            break;
                        }

                    case WalEntryType.UpdateUser:
                        {
                            var user = ParseRecord(entry.Payload);
                            DebugLog?.Invoke($"[WAL] Replay UpdateUser: {user.UserName}");
                            _users[user.UserName] = user;
                            break;
                        }

                    case WalEntryType.DeleteUser:
                        {
                            var user = Encoding.UTF8.GetString(entry.Payload);
                            DebugLog?.Invoke($"[WAL] Replay DeleteUser: {user}");
                            _users.Remove(user);
                            break;
                        }
                }
            }
        }

        // ========================================================================
        // SNAPSHOT / COMPACTION
        // ========================================================================
        private void RewriteSnapshotAndClearWal()
        {
            DebugLog?.Invoke("[DB] Writing new snapshot and clearing WAL...");

            var snapshot = BuildSnapshotBytes(_users);
            AtomicSnapshot.WriteAtomic(_dbPath, snapshot);

            _wal.Clear();
        }

        // ========================================================================
        // SNAPSHOT READ
        // ========================================================================

        private Dictionary<string, FtpUser> LoadDb()
        {
            var raw = File.ReadAllBytes(_dbPath);
            var decrypted = DecryptSnapshot(raw);
            var decompressed = Lz4Codec.Decompress(decrypted);

            using var ms = new MemoryStream(decompressed);
            using var br = new BinaryReader(ms);

            return ParseSnapshotData(br);
        }

        private Dictionary<string, FtpUser> ParseSnapshotData(BinaryReader br)
        {
            var count = br.ReadUInt32();
            var dict = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < count; i++)
            {
                var len = br.ReadUInt32();
                var recBytes = br.ReadBytes((int)len);

                using var recMs = new MemoryStream(recBytes);
                using var recBr = new BinaryReader(recMs);

                var type = recBr.ReadByte();
                if (type != 0)
                    continue; // unknown record

                var user = ParseRecordFromReader(recBr);
                dict[user.UserName] = user;
            }
            return dict;
        }

        // ========================================================================
        // SNAPSHOT WRITE
        // ========================================================================
        private byte[] BuildSnapshotBytes(Dictionary<string, FtpUser> users)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((uint)users.Count);

            foreach (var record in users.Values.Select(u => BuildRecordIncludingType(u)))
            {
                bw.Write((uint)record.Length);
                bw.Write(record);
            }

            var raw = ms.ToArray();
            var compressed = Lz4Codec.Compress(raw);
            return EncryptSnapshot(compressed);
        }

        // ========================================================================
        // RECORD SERIALIZATION (v1 format, unchanged)
        // ========================================================================
        private byte[] BuildRecord(FtpUser u)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            var nameLen = (ushort)Encoding.UTF8.GetByteCount(u.UserName);
            var passLen = (ushort)Encoding.UTF8.GetByteCount(u.PasswordHash);
            var homeLen = (ushort)Encoding.UTF8.GetByteCount(u.HomeDir);
            var groupLen = (ushort)(u.GroupName is null ? 0 : Encoding.UTF8.GetByteCount(u.GroupName));

            var ipMaskBytes = u.AllowedIpMask == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.AllowedIpMask);
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

            bw.Write((ushort)ipMaskBytes.Length);
            bw.Write((ushort)identBytes.Length);

            bw.Write(Encoding.UTF8.GetBytes(u.UserName));
            bw.Write(Encoding.UTF8.GetBytes(u.PasswordHash));
            bw.Write(Encoding.UTF8.GetBytes(u.HomeDir));
            if (groupLen > 0) bw.Write(Encoding.UTF8.GetBytes(u.GroupName!));
            if (ipMaskBytes.Length > 0) bw.Write(ipMaskBytes);
            if (identBytes.Length > 0) bw.Write(identBytes);

            return ms.ToArray();
        }

        private byte[] BuildRecordIncludingType(FtpUser u)
        {
            var raw = BuildRecord(u);
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((byte)0); // record type
            bw.Write(raw);

            return ms.ToArray();
        }

        // ========================================================================
        // RECORD PARSING
        // ========================================================================
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

            var user = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
            var pass = Encoding.UTF8.GetString(br.ReadBytes(passLen));
            var home = Encoding.UTF8.GetString(br.ReadBytes(homeLen));

            var group = groupLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(groupLen)) : null;
            var ipmask = ipMaskLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(ipMaskLen)) : null;
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
                GroupName: group,
                CreditsKb: credits,
                AllowedIpMask: ipmask,
                RequireIdentMatch: (flags & 32) != 0,
                RequiredIdent: ident
            );
        }

        // ========================================================================
        // ENCRYPTION HELPERS
        // ========================================================================
        private byte[] EncryptSnapshot(byte[] buf)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[buf.Length];

            using (var gcm = new AesGcm(_masterKey))
                gcm.Encrypt(nonce, buf, ciphertext, tag);

            using var ms = new MemoryStream();
            ms.Write(nonce);
            ms.Write(ciphertext);
            ms.Write(tag);
            return ms.ToArray();
        }

        private byte[] DecryptSnapshot(byte[] buf)
        {
            ReadOnlySpan<byte> nonce = buf[..12];
            ReadOnlySpan<byte> tag = buf[^16..];
            ReadOnlySpan<byte> ciphertext = buf[12..^16];

            var plaintext = new byte[ciphertext.Length];

            using var gcm = new AesGcm(_masterKey);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }

        private byte[] DeriveKey(byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(_masterPasswordBytes, salt, 200_000, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32);
        }

        private byte[] EnsureSaltFile(string saltPath)
        {
            if (File.Exists(saltPath))
                return File.ReadAllBytes(saltPath);

            var salt = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(saltPath, salt);
            return salt;
        }

        // ========================================================================
        // HOT RELOAD
        // ========================================================================
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
                        DebugLog?.Invoke("[DB] Hot reload triggered.");
                        var updated = LoadDb();
                        foreach (var kv in updated)
                            _users[kv.Key] = kv.Value;
                    }
                    catch
                    {
                        DebugLog?.Invoke("[DB] Hot reload failed (corrupted write?)");
                    }
                }
            };

            _watcher.EnableRaisingEvents = true;
        }
    }

}
