using amFTPd.Security;
using System.Text.Json;

namespace amFTPd.Config.Ftpd
{
    public sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<string, FtpUser> _users;
        private readonly Dictionary<string, int> _currentLogins = new();
        private readonly Lock _sync = new();
        private readonly string _configPath;

        private InMemoryUserStore(Dictionary<string, FtpUser> users, string configPath)
        {
            _users = users;
            _configPath = configPath;
        }

        public static InMemoryUserStore LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                // Seed default admin: user "admin", password "admin"
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
                    CreditsKb: 1024 * 1024,    // 1 GB credits, tweak if you want
                    AllowedIpMask: null,
                    RequireIdentMatch: false,
                    RequiredIdent: null
                );

                var dict = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase)
                {
                    [admin.UserName] = admin
                };

                var store = new InMemoryUserStore(dict, path);
                store.Save(); // create file
                return store;
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<FtpUserConfig>(json) ?? FtpUserConfig.Empty;

            var users = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in cfg.Users)
            {
                users[u.UserName] = new FtpUser(
                    UserName: u.UserName,
                    PasswordHash: u.PasswordHash,
                    HomeDir: u.HomeDir,
                    IsAdmin: u.IsAdmin,
                    AllowFxp: u.AllowFxp,
                    AllowUpload: u.AllowUpload,
                    AllowDownload: u.AllowDownload,
                    AllowActiveMode: u.AllowActiveMode,
                    MaxConcurrentLogins: u.MaxConcurrentLogins,
                    IdleTimeout: TimeSpan.FromSeconds(u.IdleTimeoutSeconds),
                    MaxUploadKbps: u.MaxUploadKbps,
                    MaxDownloadKbps: u.MaxDownloadKbps,
                    GroupName: u.GroupName,
                    CreditsKb: u.CreditsKb,
                    AllowedIpMask: u.AllowedIpMask,
                    RequireIdentMatch: u.RequireIdentMatch,
                    RequiredIdent: u.RequiredIdent
                );
            }

            return new InMemoryUserStore(users, path);
        }

        private void Save()
        {
            var cfg = new FtpUserConfig(
                _users.Values
                    .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                    .Select(u => new FtpUserConfigUser(
                        UserName: u.UserName,
                        PasswordHash: u.PasswordHash,
                        HomeDir: u.HomeDir,
                        IsAdmin: u.IsAdmin,
                        AllowFxp: u.AllowFxp,
                        AllowUpload: u.AllowUpload,
                        AllowDownload: u.AllowDownload,
                        AllowActiveMode: u.AllowActiveMode,
                        MaxConcurrentLogins: u.MaxConcurrentLogins,
                        IdleTimeoutSeconds: (int)u.IdleTimeout.TotalSeconds,
                        MaxUploadKbps: u.MaxUploadKbps,
                        MaxDownloadKbps: u.MaxDownloadKbps,
                        GroupName: u.GroupName,
                        CreditsKb: u.CreditsKb,
                        AllowedIpMask: u.AllowedIpMask,
                        RequireIdentMatch: u.RequireIdentMatch,
                        RequiredIdent: u.RequiredIdent
                    ))
                    .ToList()
            );

            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_configPath, json);
        }

        public bool TryAuthenticate(string user, string password, out FtpUser? account)
        {
            account = null;

            if (!_users.TryGetValue(user, out var u))
                return false;

            if (!PasswordHasher.VerifyPassword(password, u.PasswordHash))
                return false;

            lock (_sync)
            {
                var count = _currentLogins.TryGetValue(u.UserName, out var c) ? c : 0;
                if (u.MaxConcurrentLogins > 0 && count >= u.MaxConcurrentLogins)
                    return false;

                _currentLogins[u.UserName] = count + 1;
            }

            account = u;
            return true;
        }

        public void OnLogout(FtpUser user)
        {
            lock (_sync)
            {
                if (_currentLogins.TryGetValue(user.UserName, out var c) && c > 0)
                    _currentLogins[user.UserName] = c - 1;
            }
        }

        public FtpUser? FindUser(string userName)
            => _users.TryGetValue(userName, out var u) ? u : null;

        public IEnumerable<FtpUser> GetAllUsers()
        {
            lock (_sync)
            {
                return _users.Values
                    .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

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
                Save();
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
                    error = "User does not exist.";
                    return false;
                }

                _users[user.UserName] = user;
                Save();
            }

            error = null;
            return true;
        }
    }
}
