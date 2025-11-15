using amFTPd.Config.Ftpd;
using amFTPd.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace amFTPd.Utils
{
    public sealed class BinaryUserStore : IUserStore
    {
        private readonly string _path;
        private readonly byte[] _masterPasswordBytes;
        private readonly Dictionary<string, FtpUser> _users;
        private readonly Dictionary<string, int> _loginCounter = new();
        private readonly object _sync = new();

        public BinaryUserStore(string path, string masterPassword)
        {
            _path = path;
            _masterPasswordBytes = Encoding.UTF8.GetBytes(masterPassword);

            if (File.Exists(path))
                _users = LoadDb();
            else
                _users = CreateEmptyDb();
        }

        // ========================================================================
        // PUBLIC INTERFACE
        // ========================================================================

        public bool TryAuthenticate(string user, string password, out FtpUser? account)
        {
            account = null;
            if (!_users.TryGetValue(user, out var u))
                return false;

            if (!PasswordHasher.VerifyPassword(password, u.PasswordHash))
                return false;

            lock (_sync)
            {
                int count = _loginCounter.TryGetValue(u.UserName, out var c) ? c : 0;
                if (u.MaxConcurrentLogins > 0 && count >= u.MaxConcurrentLogins)
                    return false;
                _loginCounter[u.UserName] = count + 1;
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

        public FtpUser? FindUser(string userName)
            => _users.TryGetValue(userName, out var u) ? u : null;

        public IEnumerable<FtpUser> GetAllUsers() => _users.Values;

        public bool TryAddUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (_users.ContainsKey(user.UserName))
                {
                    error = "Exists.";
                    return false;
                }

                _users[user.UserName] = user;
                SaveDb();
            }
            error = null;
            return true;
        }

        public bool TryUpdateUser(FtpUser user, out string? error)
        {
            lock (_sync)
            {
                if (!_users.ContainsKey(user.UserName))
                {
                    error = "Not found.";
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
            SaveDbSnapshot(d);
            return d;
        }

        private Dictionary<string, FtpUser> LoadDb()
        {
            using var fs = File.OpenRead(_path);
            var header = new byte[6];
            fs.Read(header);

            if (Encoding.ASCII.GetString(header) != "AMFTP1")
                throw new Exception("Invalid DB header.");

            int version = fs.ReadByte();
            if (version != 1)
                throw new Exception("Unsupported DB version.");

            var salt = new byte[32];
            fs.Read(salt);

            var reserved = new byte[16];
            fs.Read(reserved);

            var encrypted = fs.ReadAllRemaining();

            // Decrypt
            var decrypted = DecryptDb(encrypted, salt);

            using var ms = new MemoryStream(decrypted);
            using var br = new BinaryReader(ms);

            uint count = br.ReadUInt32();

            var output = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                uint len = br.ReadUInt32();
                long start = ms.Position;

                byte type = br.ReadByte();
                if (type != 0)
                    throw new Exception("Unknown record type");

                string ReadStr(ushort len)
                    => Encoding.UTF8.GetString(br.ReadBytes(len));

                ushort nameLen = br.ReadUInt16();
                ushort passLen = br.ReadUInt16();
                ushort homeLen = br.ReadUInt16();
                ushort groupLen = br.ReadUInt16();

                int flags = br.ReadInt32();
                int maxLogins = br.ReadInt32();
                int idleSec = br.ReadInt32();
                int up = br.ReadInt32();
                int down = br.ReadInt32();
                long credits = br.ReadInt64();

                ushort ipMaskLen = br.ReadUInt16();
                ushort identLen = br.ReadUInt16();

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

                    void WriteStr(string? s)
                    {
                        if (s is null)
                        {
                            wr.Write((ushort)0);
                            return;
                        }

                        var b = Encoding.UTF8.GetBytes(s);
                        wr.Write((ushort)b.Length);
                        wr.Write(b);
                    }

                    var nameB = Encoding.UTF8.GetBytes(u.UserName);
                    var passB = Encoding.UTF8.GetBytes(u.PasswordHash);
                    var homeB = Encoding.UTF8.GetBytes(u.HomeDir);
                    var groupB = u.GroupName is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.GroupName);

                    // Write strings
                    wr.Write((ushort)nameB.Length);
                    wr.Write((ushort)passB.Length);
                    wr.Write((ushort)homeB.Length);
                    wr.Write((ushort)groupB.Length);

                    // Flags
                    int flags =
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

                    var ipB = u.AllowedIpMask is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.AllowedIpMask);
                    var identB = u.RequiredIdent is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(u.RequiredIdent);

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

            bw2.Write(Encoding.ASCII.GetBytes("AMFTP1"));
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
            byte[] key = DeriveKey(salt);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);

            using var gcm = new AesGcm(key);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            gcm.Encrypt(nonce, plaintext, ciphertext, tag);

            using var ms = new MemoryStream();
            ms.Write(nonce);
            ms.Write(ciphertext);
            ms.Write(tag);
            return ms.ToArray();
        }

        private byte[] DecryptDb(ReadOnlySpan<byte> encrypted, byte[] salt)
        {
            byte[] key = DeriveKey(salt);

            ReadOnlySpan<byte> nonce = encrypted[..12];
            ReadOnlySpan<byte> tag = encrypted[^16..];
            ReadOnlySpan<byte> ciphertext = encrypted[12..^16];

            byte[] plaintext = new byte[ciphertext.Length];

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
    }
}
