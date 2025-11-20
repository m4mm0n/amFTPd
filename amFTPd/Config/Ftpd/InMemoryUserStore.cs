/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
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
        #region Private Fields

        private readonly Dictionary<string, FtpUser> _users;
        private readonly Dictionary<string, int> _currentLogins = new();
        private readonly Lock _sync = new();
        private readonly string _configPath;

        #endregion
        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryUserStore"/> class with the specified user dictionary
        /// and configuration path.
        /// </summary>
        /// <remarks>This constructor sets up the in-memory user store with the provided user data and
        /// configuration path. Ensure that the <paramref name="users"/> dictionary is populated with valid user entries
        /// before using this instance.</remarks>
        /// <param name="users">A dictionary containing user data, where the key is the username and the value is an <see cref="FtpUser"/>
        /// object. Cannot be null.</param>
        /// <param name="configPath">The file path to the configuration file. Cannot be null or empty.</param>
        private InMemoryUserStore(Dictionary<string, FtpUser> users, string configPath)
        {
            _users = users;
            _configPath = configPath;
        }
        /// <summary>
        /// Loads an <see cref="InMemoryUserStore"/> from the specified file path. If the file does not exist, a new
        /// store is created with a default admin user and saved to the file.
        /// </summary>
        /// <remarks>The default admin user is created with the username "admin" and password "admin".
        /// This user has administrative privileges and a home directory set to the root ("/"). It is recommended to
        /// change the default password after initialization for security purposes.</remarks>
        /// <param name="path">The path to the file containing the user store configuration. If the file does not exist, it will be created
        /// with default settings.</param>
        /// <returns>An <see cref="InMemoryUserStore"/> instance populated with users from the specified file, or a new store
        /// with a default admin user if the file does not exist.</returns>
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
        /// <summary>
        /// Attempts to authenticate a user with the specified username and password.
        /// </summary>
        /// <remarks>This method verifies the provided username and password against the stored user data.
        /// If the authentication succeeds, the method ensures that the user has not exceeded their maximum allowed
        /// concurrent logins. The method is thread-safe.</remarks>
        /// <param name="user">The username of the user attempting to authenticate.</param>
        /// <param name="password">The password associated with the specified username.</param>
        /// <param name="account">When this method returns, contains the authenticated <see cref="FtpUser"/> object if authentication
        /// succeeds; otherwise, <see langword="null"/>. This parameter is passed uninitialized.</param>
        /// <returns><see langword="true"/> if the user is successfully authenticated; otherwise, <see langword="false"/>.</returns>
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
        /// <summary>
        /// Handles the logout process for the specified FTP user, decrementing their active login count.
        /// </summary>
        /// <remarks>This method ensures that the active login count for the specified user is decremented
        /// in a thread-safe manner. If the user does not have any active logins, no changes are made.</remarks>
        /// <param name="user">The FTP user who is logging out. Cannot be <see langword="null"/>.</param>
        public void OnLogout(FtpUser user)
        {
            lock (_sync)
                if (_currentLogins.TryGetValue(user.UserName, out var c) && c > 0)
                    _currentLogins[user.UserName] = c - 1;
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
        /// Retrieves all FTP users in the system, sorted by username in a case-insensitive manner.
        /// </summary>
        /// <remarks>The returned collection is a snapshot of the current users at the time of the method
        /// call.  Changes to the user list after the method is called will not be reflected in the returned collection.
        /// This method is thread-safe.</remarks>
        /// <returns>An <see cref="IEnumerable{T}"/> containing all FTP users, sorted by username in ascending order.</returns>
        public IEnumerable<FtpUser> GetAllUsers()
        {
            lock (_sync)
                return _users.Values
                    .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }
        /// <summary>
        /// Attempts to add a new FTP user to the system.
        /// </summary>
        /// <remarks>This method ensures thread safety by locking the internal user collection during the
        /// operation. If a user with the same username already exists, the method does not add the user and sets the
        /// <paramref name="error"/> parameter to an appropriate message.</remarks>
        /// <param name="user">The <see cref="FtpUser"/> object representing the user to be added. The user's <see
        /// cref="FtpUser.UserName"/> must be unique.</param>
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
                Save();
            }

            error = null;
            return true;
        }
        /// <summary>
        /// Attempts to update the details of an existing FTP user.
        /// </summary>
        /// <remarks>This method is thread-safe. If the specified user does not exist, the update
        /// operation will fail, and an error message will be provided in the <paramref name="error"/>
        /// parameter.</remarks>
        /// <param name="user">The <see cref="FtpUser"/> object containing the updated user details. The <see cref="FtpUser.UserName"/>
        /// property must match an existing user.</param>
        /// <param name="error">When this method returns, contains an error message if the update fails; otherwise, <see langword="null"/>.
        /// This parameter is passed uninitialized.</param>
        /// <returns><see langword="true"/> if the user details were successfully updated; otherwise, <see langword="false"/>.</returns>
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
