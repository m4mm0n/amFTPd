using amFTPd.Config.Ftpd;
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
        #region Private Fields
        private readonly string _path;
        private readonly byte[] _masterPasswordBytes;
        private readonly Dictionary<string, FtpUser> _users;
        private readonly Dictionary<string, int> _loginCounter = new();
        private readonly Lock _sync = new();
        private string arcHeader = "AMFTPDBUS";
        private int arcVersion = 100;
        #endregion
        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryUserStore"/> class with the specified file path and
        /// master password.
        /// </summary>
        /// <remarks>This constructor initializes the user database by either loading it from the
        /// specified file path or creating a new empty database if the file does not exist. The master password is
        /// converted to a byte array using UTF-8 encoding for internal use.</remarks>
        /// <param name="path">The file path where the user database is stored. If the file does not exist, a new database will be created.</param>
        /// <param name="masterPassword">The master password used to secure the user database. Cannot be null or empty.</param>
        public BinaryUserStore(string path, string masterPassword)
        {
            _path = path;
            _masterPasswordBytes = Encoding.UTF8.GetBytes(masterPassword);

            _users = File.Exists(path) ? LoadDb() : CreateEmptyDb();
        }
        /// <summary>
        /// Attempts to authenticate a user with the specified username and password.
        /// </summary>
        /// <remarks>This method verifies the provided username and password against the stored user data.
        /// If the user is authenticated, it ensures that the maximum number of concurrent logins for the user has not
        /// been exceeded. The method is thread-safe.</remarks>
        /// <param name="user">The username of the user attempting to authenticate.</param>
        /// <param name="password">The password provided for authentication.</param>
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
                var count = _loginCounter.TryGetValue(u.UserName, out var c) ? c : 0;
                if (u.MaxConcurrentLogins > 0 && count >= u.MaxConcurrentLogins)
                    return false;

                _loginCounter[u.UserName] = count + 1;
            }

            account = u;
            return true;
        }
        /// <summary>
        /// Handles the logout process for the specified FTP user.
        /// </summary>
        /// <remarks>This method decrements the login counter for the specified user if the user is
        /// currently logged in. Thread safety is ensured by locking during the operation.</remarks>
        /// <param name="user">The FTP user who is logging out. Cannot be <see langword="null"/>.</param>
        public void OnLogout(FtpUser user)
        {
            lock (_sync)
                if (_loginCounter.TryGetValue(user.UserName, out var c) && c > 0)
                    _loginCounter[user.UserName] = c - 1;
        }
        /// <summary>
        /// Finds and returns the user associated with the specified username.
        /// </summary>
        /// <param name="userName">The username of the user to find. This value cannot be <see langword="null"/> or empty.</param>
        /// <returns>The <see cref="FtpUser"/> object associated with the specified username, or <see langword="null"/> if no
        /// user is found.</returns>
        public FtpUser? FindUser(string userName)
            => _users.TryGetValue(userName, out var u) ? u : null;
        /// <summary>
        /// Retrieves all users currently stored in the system.
        /// </summary>
        /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="FtpUser"/> representing all users.  The collection will be
        /// empty if no users are available.</returns>
        public IEnumerable<FtpUser> GetAllUsers() => _users.Values;
        /// <summary>
        /// Attempts to add a new FTP user to the system.
        /// </summary>
        /// <remarks>This method ensures thread safety by locking the internal user collection during the
        /// operation. If a user with the same username already exists, the method does not add the user and sets the
        /// <paramref name="error"/> parameter to an appropriate message.</remarks>
        /// <param name="user">The <see cref="FtpUser"/> object representing the user to be added. The <see cref="FtpUser.UserName"/>
        /// property must be unique.</param>
        /// <param name="error">When the method returns, contains an error message if the operation fails; otherwise, <see
        /// langword="null"/>.</param>
        /// <returns><see langword="true"/> if the user was successfully added; otherwise, <see langword="false"/>.</returns>
        public bool TryAddUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (_users.ContainsKey(user.UserName))
                {
                    error = "User already exists.";
                    return false;
                }

                _users[user.UserName] = user;
                SaveDb();
            }

            error = null;
            return true;
        }
        /// <summary>
        /// Attempts to update the details of an existing FTP user.
        /// </summary>
        /// <remarks>This method is thread-safe. If the specified user does not exist, the update will
        /// fail, and an error message will be provided in the <paramref name="error"/> parameter.</remarks>
        /// <param name="user">The <see cref="FtpUser"/> object containing the updated user details. The user's username must match an
        /// existing user.</param>
        /// <param name="error">When this method returns, contains an error message if the update fails; otherwise, <see langword="null"/>.
        /// This parameter is passed uninitialized.</param>
        /// <returns><see langword="true"/> if the user was successfully updated; otherwise, <see langword="false"/>.</returns>
        public bool TryUpdateUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (!_users.ContainsKey(user.UserName))
                {
                    error = "User does not exist.";
                    return false;
                }

                _users[user.UserName] = user;
                SaveDb();
            }

            error = null;
            return true;
        }

        // ========================================================================
        // INTERNAL DB LOAD/SAVE
        // ========================================================================

        private Dictionary<string, FtpUser> CreateEmptyDb()
        {
            var d = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);

            // seed default admin (you can tweak or remove this if you want empty)
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
                CreditsKb: 1024 * 1024,
                AllowedIpMask: null,
                RequireIdentMatch: false,
                RequiredIdent: null
            );

            d[admin.UserName] = admin;

            SaveDbSnapshot(d);
            return d;
        }

        private Dictionary<string, FtpUser> LoadDb()
        {
            using var fs = File.OpenRead(_path);

            var header = new byte[arcHeader.Length];
            fs.ReadExactly(header);

            if (Encoding.ASCII.GetString(header) != arcHeader)
                throw new Exception("Invalid DB header.");

            var version = fs.ReadByte();
            if (version != arcVersion)
                throw new Exception("Unsupported DB version.");

            var salt = new byte[32];
            fs.ReadExactly(salt, 0, salt.Length);

            var reserved = new byte[16];
            fs.ReadExactly(reserved, 0, reserved.Length);

            // Read remaining bytes (encrypted)
            var encrypted = ReadAll(fs);

            var decrypted = DecryptDb(encrypted, salt);

            using var ms = new MemoryStream(decrypted);
            using var br = new BinaryReader(ms);

            var count = br.ReadUInt32();
            var output = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < count; i++)
            {
                var len = br.ReadUInt32();
                var start = ms.Position;

                var type = br.ReadByte();
                if (type != 0)
                    throw new Exception("Unknown record type");

                string ReadStr(ushort length)
                    => Encoding.UTF8.GetString(br.ReadBytes(length));

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

                var userName = ReadStr(nameLen);
                var passHash = ReadStr(passLen);
                var homeDir = ReadStr(homeLen);
                var group = groupLen > 0 ? ReadStr(groupLen) : null;
                var ipmask = ipMaskLen > 0 ? ReadStr(ipMaskLen) : null;
                var ident = identLen > 0 ? ReadStr(identLen) : null;

                var u = new FtpUser(
                    UserName: userName,
                    PasswordHash: passHash,
                    HomeDir: homeDir,
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

                output[userName] = u;

                ms.Position = start + len;
            }

            return output;
        }

        private void SaveDb()
        {
            SaveDbSnapshot(_users);
        }

        private void SaveDbSnapshot(Dictionary<string, FtpUser> users)
        {
            var salt = RandomNumberGenerator.GetBytes(32);

            byte[] plaintext;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((uint)users.Count);

                foreach (var u in users.Values)
                {
                    using var rec = new MemoryStream();
                    using var wr = new BinaryWriter(rec);

                    wr.Write((byte)0); // record type

                    var nameB = Encoding.UTF8.GetBytes(u.UserName);
                    var passB = Encoding.UTF8.GetBytes(u.PasswordHash);
                    var homeB = Encoding.UTF8.GetBytes(u.HomeDir);
                    var groupB = u.GroupName is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.GroupName);
                    var ipB = u.AllowedIpMask is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.AllowedIpMask);
                    var identB = u.RequiredIdent is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.RequiredIdent);

                    wr.Write((ushort)nameB.Length);
                    wr.Write((ushort)passB.Length);
                    wr.Write((ushort)homeB.Length);
                    wr.Write((ushort)groupB.Length);

                    var flags =
                        (u.IsAdmin ? 1 : 0) |
                        (u.AllowFxp ? 2 : 0) |
                        (u.AllowUpload ? 4 : 0) |
                        (u.AllowDownload ? 8 : 0) |
                        (u.AllowActiveMode ? 16 : 0) |
                        (u.RequireIdentMatch ? 32 : 0);

                    wr.Write(flags);
                    wr.Write(u.MaxConcurrentLogins);
                    wr.Write((int)u.IdleTimeout.TotalSeconds);
                    wr.Write(u.MaxUploadKbps);
                    wr.Write(u.MaxDownloadKbps);
                    wr.Write(u.CreditsKb);

                    wr.Write((ushort)ipB.Length);
                    wr.Write((ushort)identB.Length);

                    wr.Write(nameB);
                    wr.Write(passB);
                    wr.Write(homeB);
                    wr.Write(groupB);
                    wr.Write(ipB);
                    wr.Write(identB);

                    var recBytes = rec.ToArray();
                    bw.Write((uint)recBytes.Length);
                    bw.Write(recBytes);
                }

                plaintext = ms.ToArray();
            }

            var encrypted = EncryptDb(plaintext, salt);

            using var fs = File.Create(_path);
            using var bw2 = new BinaryWriter(fs);

            bw2.Write(Encoding.ASCII.GetBytes(arcHeader));
            bw2.Write((byte)1);
            bw2.Write(salt);
            bw2.Write(new byte[16]); // reserved
            bw2.Write(encrypted);
        }

        // ========================================================================
        // AES-GCM Encryption/Decryption
        // ========================================================================

        private byte[] EncryptDb(ReadOnlySpan<byte> plaintext, byte[] salt)
        {
            var key = DeriveKey(salt);
            var nonce = RandomNumberGenerator.GetBytes(12);

            using var gcm = new AesGcm(key);

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];

            gcm.Encrypt(nonce, plaintext, ciphertext, tag);

            using var ms = new MemoryStream();
            ms.Write(nonce);
            ms.Write(ciphertext);
            ms.Write(tag);
            return ms.ToArray();
        }

        private byte[] DecryptDb(ReadOnlySpan<byte> encrypted, byte[] salt)
        {
            var key = DeriveKey(salt);

            var nonce = encrypted[..12];
            var tag = encrypted[^16..];
            var ciphertext = encrypted[12..^16];

            var plaintext = new byte[ciphertext.Length];

            using var gcm = new AesGcm(key);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
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

        // Helper: read all remaining bytes from a stream
        private static byte[] ReadAll(Stream s)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
