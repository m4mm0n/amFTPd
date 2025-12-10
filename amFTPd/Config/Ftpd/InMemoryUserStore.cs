/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           InMemoryUserStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-10 03:58:32
 *  CRC32:          0xE3EE8133
 *  
 *  Description:
 *      Represents an in-memory user store for managing FTP user accounts.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Security;
using System.Text.Json;

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Represents an in-memory user store for managing FTP user accounts.
    /// </summary>
    /// <remarks>This class provides functionality to manage FTP users, including authentication, user
    /// retrieval,  and user management operations such as adding, updating, and removing users. The user data is 
    /// stored in memory and can be persisted to a file for later retrieval. This implementation is  thread-safe and
    /// supports concurrent access.</remarks>
    public sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<string, FtpUser> _users;
        private readonly string _configPath;
        private readonly Dictionary<string, int> _activeLogins = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _loginLock = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public InMemoryUserStore(Dictionary<string, FtpUser> users, string configPath)
        {
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        }

        // =====================================================================
        // Static loader used by AmFtpdConfigLoader
        // =====================================================================

        public static InMemoryUserStore LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("UsersDbPath must not be empty.", nameof(path));

            if (!File.Exists(path))
            {
                // First run: create default admin
                var dict = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);
                var admin = CreateDefaultAdminUser();
                dict[admin.UserName] = admin;

                var store = new InMemoryUserStore(dict, path);
                store.Save();
                return store;
            }

            try
            {
                var json = File.ReadAllText(path);
                var cfgUsers = JsonSerializer.Deserialize<List<FtpUserConfigUser>>(json, JsonOptions)
                               ?? new List<FtpUserConfigUser>();

                var users = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);

                foreach (var cu in cfgUsers)
                {
                    var user = FromConfig(cu);
                    users[user.UserName] = user;
                }

                return new InMemoryUserStore(users, path);
            }
            catch
            {
                // Corrupt file → fall back to a minimal admin-only store.
                var dict = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);
                var admin = CreateDefaultAdminUser();
                dict[admin.UserName] = admin;

                return new InMemoryUserStore(dict, path);
            }
        }

        // =====================================================================
        // Persistence
        // =====================================================================

        public void Save()
        {
            var list = _users.Values
                .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                .Select(ToConfig)
                .ToList();

            var json = JsonSerializer.Serialize(list, JsonOptions);

            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_configPath, json);
        }

        // =====================================================================
        // IUserStore implementation
        // =====================================================================

        public FtpUser? FindUser(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return null;

            return _users.TryGetValue(userName, out var u) ? u : null;
        }

        public bool TryAuthenticate(string userName, string password, out FtpUser? user)
        {
            user = null;

            if (!_users.TryGetValue(userName, out var acc))
                return false;

            if (acc.Disabled)
                return false;

            if (!VerifyPassword(acc.PasswordHash, password))
                return false;

            // ----- NEW: enforce MaxConcurrentLogins -----
            if (acc.MaxConcurrentLogins > 0)
            {
                lock (_loginLock)
                {
                    var current = _activeLogins.TryGetValue(userName, out var c) ? c : 0;
                    if (current >= acc.MaxConcurrentLogins)
                    {
                        // too many sessions
                        return false;
                    }

                    _activeLogins[userName] = current + 1;
                }
            }
            // --------------------------------------------

            user = acc;
            return true;
        }

        public bool TryAddUser(FtpUser user, out string? error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(user.UserName))
            {
                error = "UserName must not be empty.";
                return false;
            }

            if (_users.ContainsKey(user.UserName))
            {
                error = "User already exists.";
                return false;
            }

            _users[user.UserName] = user;
            Save();
            return true;
        }

        public bool TryUpdateUser(FtpUser user, out string? error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(user.UserName))
            {
                error = "UserName must not be empty.";
                return false;
            }

            _users[user.UserName] = user;
            Save();
            return true;
        }

        public void OnLogout(FtpUser user)
        {
            if (user is null)
                return;

            if (user.MaxConcurrentLogins > 0)
            {
                lock (_loginLock)
                {
                    if (_activeLogins.TryGetValue(user.UserName, out var c))
                    {
                        c--;
                        if (c <= 0)
                        {
                            _activeLogins.Remove(user.UserName);
                        }
                        else
                        {
                            _activeLogins[user.UserName] = c;
                        }
                    }
                }
            }

            // If you want "last logout" persistence etc, do it here.
            Save();
        }

        public IEnumerable<FtpUser> GetAllUsers()
            => _users.Values;

        // Optional helper for admin / SITE commands
        public bool TryDeleteUser(string userName, out string? error)
        {
            error = null;
            if (!_users.Remove(userName))
            {
                error = "User not found.";
                return false;
            }

            Save();
            return true;
        }

        // =====================================================================
        // Mapping: FtpUserConfigUser <-> FtpUser
        // =====================================================================

        private static FtpUser FromConfig(FtpUserConfigUser cu)
        {
            var idle = cu.IdleTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(cu.IdleTimeoutSeconds)
                : (TimeSpan?)null;

            // ctor: FtpUser(string, string, bool, string, string, IReadOnlyList<string>,
            //               bool, bool, bool, bool, bool, bool,
            //               string, string, TimeSpan?, int, int, long,
            //               IReadOnlyList<FtpSection>, int, bool, string?)
            return new FtpUser(
                cu.UserName,                                      // userName
                cu.PasswordHash,                                  // passwordHash
                cu.Disabled,                                      // disabled
                cu.HomeDir,                                       // homeDir
                cu.GroupName,                                     // primary group (by position)
                cu.SecondaryGroups ?? Array.Empty<string>(),      // secondary groups
                cu.IsAdmin || cu.IsAdministrator,                 // isAdmin
                cu.AllowFxp,                                      // allowFxp
                cu.AllowUpload,                                   // allowUpload
                cu.AllowDownload,                                 // allowDownload
                cu.AllowActiveMode,                               // allowActiveMode
                cu.RequireIdentMatch,                             // requireIdentMatch
                cu.AllowedIpMask ?? string.Empty,                 // allowedIpMask
                cu.RequiredIdent ?? string.Empty,                 // requiredIdent
                idle,                                             // idleTimeout
                cu.MaxUploadKbps,                                 // maxUploadKbps
                cu.MaxDownloadKbps,                               // maxDownloadKbps
                cu.CreditsKb,                                     // creditsKb
                Array.Empty<FtpSection>(),                        // sections (none from JSON)
                cu.MaxConcurrentLogins,                           // maxConcurrentLogins
                IsNoRatio: false,                                 // isNoRatio
                FlagsRaw: string.Empty                            // flagsRaw (for SITE FLAGS etc.)
            );
        }

        private static FtpUserConfigUser ToConfig(FtpUser u)
        {
            return new FtpUserConfigUser(
                UserName: u.UserName,
                PasswordHash: u.PasswordHash,
                Disabled: u.Disabled,
                HomeDir: u.HomeDir,
                GroupName: u.GroupName ?? string.Empty,
                SecondaryGroups: u.SecondaryGroups ?? Array.Empty<string>(),
                IsAdmin: u.IsAdmin,
                IsAdministrator: u.IsAdmin,
                AllowFxp: u.AllowFxp,
                AllowUpload: u.AllowUpload,
                AllowDownload: u.AllowDownload,
                AllowActiveMode: u.AllowActiveMode,
                RequireIdentMatch: u.RequireIdentMatch,
                AllowedIpMask: u.AllowedIpMask,
                RequiredIdent: u.RequiredIdent,
                IdleTimeoutSeconds: u.IdleTimeout.HasValue
                    ? (int)u.IdleTimeout.Value.TotalSeconds
                    : 0,
                MaxUploadKbps: u.MaxUploadKbps,
                MaxDownloadKbps: u.MaxDownloadKbps,
                CreditsKb: u.CreditsKb,
                MaxConcurrentLogins: u.MaxConcurrentLogins
            );
        }

        private static FtpUser CreateDefaultAdminUser()
        {
            return new FtpUser(
                "admin",                                  // userName
                PasswordHasher.HashPassword("admin"),     // passwordHash
                Disabled: false,
                HomeDir: "/",
                "admins",                                 // primary group (by position)
                Array.Empty<string>(),                    // secondary groups
                IsAdmin: true,
                AllowFxp: false,
                AllowUpload: true,
                AllowDownload: true,
                AllowActiveMode: true,
                RequireIdentMatch: false,
                AllowedIpMask: string.Empty,
                RequiredIdent: string.Empty,
                IdleTimeout: TimeSpan.FromMinutes(30),
                MaxUploadKbps: 0,
                MaxDownloadKbps: 0,
                CreditsKb: 1024 * 1024,                   // 1 GB
                Sections: Array.Empty<FtpSection>(),
                MaxConcurrentLogins: 5,
                IsNoRatio: false,
                FlagsRaw: string.Empty
            );
        }

        // =====================================================================
        // Password verification helper (no VerifyHashedPassword in hasher)
        // =====================================================================
        private static bool VerifyPassword(string storedHash, string password)
        {
            return string.IsNullOrEmpty(storedHash)
                ? string.IsNullOrEmpty(password)
                :
                // Accept either plain-text (for old configs)…
                string.Equals(storedHash, password, StringComparison.Ordinal) ||
                // …or PBKDF2-SHA256 formatted hashes.
                PasswordHasher.VerifyPassword(password, storedHash);
        }
    }
}
