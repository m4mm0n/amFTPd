/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-12-01
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

using System.Collections.Immutable;
using amFTPd.Config.Ftpd;
using amFTPd.Security;
using static amFTPd.Db.DbFsckDeep;
using static amFTPd.Db.DbRepair;

namespace amFTPd.Db
{
    /// <summary>
    /// Provides a centralized manager for handling user, group, and section data stores, as well as performing database
    /// maintenance, backups, and restores.
    /// </summary>
    /// <remarks>The <see cref="DatabaseManager"/> class is designed to manage and interact with multiple data
    /// stores, including users, groups, and sections. It provides functionality for loading and initializing the
    /// stores, performing integrity checks, creating backups, restoring data, and managing snapshots. This class is
    /// thread-safe for read operations, but callers should ensure thread safety for write operations if used
    /// concurrently.  Use the <see cref="Load"/> method to initialize an instance of <see cref="DatabaseManager"/> with
    /// the required base directory and master password. The class also supports optional debugging via the <see
    /// cref="DebugLog"/> action.</remarks>
    public sealed class DatabaseManager : IDisposable
    {
        // ============================================================
        // PROPERTIES
        // ============================================================

        private readonly DbInstanceLock? _instanceLock;

        public IUserStore Users { get; private set; }
        public IGroupStore Groups { get; private set; }
        public ISectionStore Sections { get; private set; }

        public string BaseDirectory { get; }
        public string MasterPassword { get; }

        public Action<string>? DebugLog;

        // The manager holds references to all stores
        private DatabaseManager(
            string baseDir,
            string masterPassword,
            IUserStore users,
            IGroupStore groups,
            ISectionStore sections,
            DbInstanceLock? instanceLock)
        {
            BaseDirectory = baseDir;
            MasterPassword = masterPassword;

            Users = users;
            Groups = groups;
            Sections = sections;
            _instanceLock = instanceLock;
        }

        // ============================================================
        // FACTORY INITIALIZER
        // ============================================================

        public static DatabaseManager Load(
            string baseDir,
            string masterPassword,
            bool useMmapForUsers = true,
            Action<string>? debugLog = null)
        {
            Directory.CreateDirectory(baseDir);

            DbInstanceLock? instanceLock = null;
            try
            {
                instanceLock = DbInstanceLock.Acquire(baseDir, debugLog);
            }
            catch
            {
                // Re-throw so the caller knows binary DB cannot be used.
                // The config loader will decide if it should fall back to JSON.
                throw;
            }

            debugLog?.Invoke("[DB-MANAGER] Loading stores…");

            // DB file paths
            var usersDb = Path.Combine(baseDir, "amftpd-users.db");
            var groupsDb = Path.Combine(baseDir, "amftpd-groups.db");
            var sectionsDb = Path.Combine(baseDir, "amftpd-sections.db");

            //
            // USER STORE
            //
            IUserStore users;
            try
            {
                users = useMmapForUsers
                    ? new BinaryUserStoreMmap(usersDb, masterPassword)
                    : new BinaryUserStore(usersDb, masterPassword);
            }
            catch (Exception ex)
            {
                debugLog?.Invoke($"[DB-MANAGER] User store failed. Falling back to memory: {ex.Message}");

                // bootstrap admin user
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
                    FlagsRaw: string.Empty
                );

                var dict = new Dictionary<string, FtpUser>(StringComparer.OrdinalIgnoreCase)
                {
                    [admin.UserName] = admin
                };

                users = new InMemoryUserStore(dict, "in-memory");
            }

            //
            // GROUP STORE
            //
            IGroupStore groups;
            try
            {
                groups = new BinaryGroupStore(groupsDb, masterPassword);
            }
            catch (Exception ex)
            {
                debugLog?.Invoke($"[DB-MANAGER] Group store failed. Falling back to memory: {ex.Message}");

                groups = new InMemoryGroupStore();

                // bootstrap admins group
                groups.TryAddGroup(
                    new FtpGroup(
                        GroupName: "admins",
                        Description: "Administrators",
                        Users: new List<string> { "admin" },
                        SectionCredits: new Dictionary<string, long>()
                    ),
                    out _
                );
            }

            //
            // SECTION STORE
            //
            ISectionStore sections;
            try
            {
                sections = new BinarySectionStore(sectionsDb, masterPassword);
            }
            catch (Exception ex)
            {
                debugLog?.Invoke($"[DB-MANAGER] Section store failed. Falling back to memory: {ex.Message}");

                var emptyManager = new SectionManager(
                    new List<Config.Ftpd.FtpSection>(),
                    "in-memory"
                );

                sections = new InMemorySectionStore(emptyManager);

                // bootstrap default section
                sections.TryAddSection(
                    new Config.Ftpd.FtpSection(
                        Name: "default",
                        VirtualRoot: "/",
                        FreeLeech: false,
                        RatioUploadUnit: 1,
                        RatioDownloadUnit: 3
                    ),
                    out _
                );
            }

            //
            // ENSURE BOOTSTRAP STATE
            //

            // Ensure admin user exists
            if (users.FindUser("admin") is null)
            {
                debugLog?.Invoke("[DB-MANAGER] Creating default admin user...");
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
                    FlagsRaw: string.Empty
                );

                users.TryAddUser(admin, out _);
            }

            // Ensure admins group exists
            if (groups.FindGroup("admins") is null)
            {
                debugLog?.Invoke("[DB-MANAGER] Creating default admins group...");
                groups.TryAddGroup(
                    new FtpGroup(
                        GroupName: "admins",
                        Description: "Administrators",
                        Users: new List<string> { "admin" },
                        SectionCredits: new Dictionary<string, long>()
                    ),
                    out _
                );
            }

            // Ensure default section exists
            if (sections.FindSection("default") is null)
            {
                debugLog?.Invoke("[DB-MANAGER] Creating default section...");
                sections.TryAddSection(
                    new Config.Ftpd.FtpSection(
                        Name: "default",
                        VirtualRoot: "/",
                        FreeLeech: false,
                        RatioUploadUnit: 1,
                        RatioDownloadUnit: 3
                    ),
                    out _
                );
            }

            //
            // CONSTRUCT MANAGER
            //
            var mgr = new DatabaseManager(baseDir, masterPassword, users, groups, sections, instanceLock)
            {
                DebugLog = debugLog
            };

            debugLog?.Invoke("[DB-MANAGER] Stores loaded successfully.");
            return mgr;
        }


        // ============================================================
        // FSCK
        // ============================================================

        public FsckResult FsckUsers() =>
            DbFsck.CheckDatabase(Path.Combine(BaseDirectory, "amftpd-users.db"), MasterPassword);

        public FsckResult FsckGroups() =>
            DbFsck.CheckDatabase(Path.Combine(BaseDirectory, "amftpd-groups.db"), MasterPassword);

        public FsckResult FsckSections() =>
            DbFsck.CheckDatabase(Path.Combine(BaseDirectory, "amftpd-sections.db"), MasterPassword);

        public DeepFsckResult FsckDeep()
            => CheckAll(Users, Groups, Sections);

        // ============================================================
        // AUTO-REPAIR
        // ============================================================

        public RepairReport Repair()
            => RepairAll(
                Users, Groups, Sections,
                BaseDirectory,
                MasterPassword
            );

        // ============================================================
        // BACKUPS (SNAPSHOT BACKUPS)
        // ============================================================

        public string BackupUsers() =>
            BackupManager.CreateBackup(
                Path.Combine(BaseDirectory, "amftpd-users.db"),
                MasterPassword
            );

        public string BackupGroups() =>
            BackupManager.CreateBackup(
                Path.Combine(BaseDirectory, "amftpd-groups.db"),
                MasterPassword
            );

        public string BackupSections() =>
            BackupManager.CreateBackup(
                Path.Combine(BaseDirectory, "amftpd-sections.db"),
                MasterPassword
            );

        public void BackupAll()
            => BackupManager.CreateBackupAll(BaseDirectory, MasterPassword);

        // ============================================================
        // RESTORE
        // ============================================================

        public void RestoreUsers(string backupFile) =>
            BackupManager.RestoreBackup(
                Path.Combine(BaseDirectory, "amftpd-users.db"),
                MasterPassword,
                backupFile
            );

        public void RestoreGroups(string backupFile) =>
            BackupManager.RestoreBackup(
                Path.Combine(BaseDirectory, "amftpd-groups.db"),
                MasterPassword,
                backupFile
            );

        public void RestoreSections(string backupFile) =>
            BackupManager.RestoreBackup(
                Path.Combine(BaseDirectory, "amftpd-sections.db"),
                MasterPassword,
                backupFile
            );

        // ============================================================
        // SNAPSHOT REBUILD (MANUAL)
        // ============================================================

        public void RebuildSnapshots()
        {
            DebugLog?.Invoke("[DB-MANAGER] Rebuilding all snapshots…");

            if (Users is BinaryUserStore bu)
                bu.ForceSnapshotRewrite();

            if (Groups is BinaryGroupStore bg)
                bg.ForceSnapshotRewrite();

            if (Sections is BinarySectionStore bs)
                bs.ForceSnapshotRewrite();

            DebugLog?.Invoke("[DB-MANAGER] Snapshot rebuild complete.");
        }

        // ============================================================
        // RELOAD (MMAP REFRESH)
        // ============================================================

        public void ReloadUsers(bool forceMmap = false)
        {
            DebugLog?.Invoke("[DB-MANAGER] Reloading user store…");

            var usersDb = Path.Combine(BaseDirectory, "amftpd-users.db");

            Users = forceMmap
                ? new BinaryUserStoreMmap(usersDb, MasterPassword)
                : new BinaryUserStore(usersDb, MasterPassword);

            DebugLog?.Invoke("[DB-MANAGER] User store reloaded.");
        }

        // ============================================================
        // DIAGNOSTICS
        // ============================================================

        public void PrintSummary()
        {
            DebugLog?.Invoke("[DB-MANAGER] Summary:");
            DebugLog?.Invoke($" Users:   {Users.GetAllUsers().Count()}");
            DebugLog?.Invoke($" Groups:  {Groups.GetAllGroups().Count()}");
            DebugLog?.Invoke($" Sections:{Sections.GetAllSections().Count()}");
            DebugLog?.Invoke(" Done.");
        }

        /// <summary>
        /// Releases the resources used by the current instance of the class.
        /// </summary>
        /// <remarks>This method disposes of the internal lock object to free unmanaged resources.  After
        /// calling this method, the instance should not be used.</remarks>
        public void Dispose()
        {
            _instanceLock?.Dispose();
        }
    }
}
