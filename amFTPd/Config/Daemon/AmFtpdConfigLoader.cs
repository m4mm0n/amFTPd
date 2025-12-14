/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdConfigLoader.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 20:49:51
 *  CRC32:          0x4DC65B5E
 *  
 *  Description:
 *      Asynchronously loads the runtime configuration for the FTP server from the specified configuration file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Config.Ftpd;
using amFTPd.Core.Dupe;
using amFTPd.Core.Fxp;
using amFTPd.Core.Race;
using amFTPd.Core.Ratio;
using amFTPd.Core.Zipscript;
using amFTPd.Credits;
using amFTPd.Db;
using amFTPd.Logging;
using amFTPd.Security;
using System.Text;
using System.Text.Json;

namespace amFTPd.Config.Daemon;

/// <summary>
/// Asynchronously loads the runtime configuration for the FTP server from the specified configuration file.
/// </summary>
/// <remarks>This method reads the configuration file, deserializes it into a structured object, and initializes
/// various components required for the FTP server, such as the user store, group store, TLS configuration, and rule
/// engines. It ensures that the server's root directory exists and creates it if necessary.  The method supports two
/// types of user store backends: binary and JSON. If the binary backend is specified, the method attempts to load the
/// database and initialize the user, group, and section stores. If the JSON backend is used, the user store is loaded
/// from the specified file, while group and section stores are managed via the configuration.  Exceptions are thrown
/// for invalid configurations, missing files, or other critical errors during initialization.</remarks>
public static class AmFtpdConfigLoader
{
    public static async Task<AmFtpdRuntimeConfig> LoadAsync(
            string configPath,
            IFtpLogger logger)
    {
        // ------------------------------------------
        // Ensure JSON config exists (generate default on first run)
        // ------------------------------------------
        if (!File.Exists(configPath))
        {
            logger.Log(FtpLogLevel.Info,
                $"Configuration file '{configPath}' not found. Creating default configuration.");

            var configDir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(configDir))
                Directory.CreateDirectory(configDir);

            // Default paths relative to app base
            var baseDir = AppContext.BaseDirectory;
            var defaultRootPath = Path.GetFullPath(Path.Combine(baseDir, "ftp-root"));
            var defaultUsersDbPath = Path.GetFullPath(Path.Combine(baseDir, "amftpd-users.json"));
            var defaultSectionsPath = Path.GetFullPath(Path.Combine(baseDir, "amftpd-sections.json"));
            var defaultGroupsDbPath = Path.GetFullPath(Path.Combine(baseDir, "amftpd-groups.db"));
            var defaultSectionsDbPath = Path.GetFullPath(Path.Combine(baseDir, "amftpd-sections.db"));
            var defaultPfxPath = Path.GetFullPath(Path.Combine(baseDir, "amftpd-cert.pfx"));

            Directory.CreateDirectory(defaultRootPath);

            // Build a minimal object that matches the JSON shape of AmFtpdConfigRoot.
            // We deliberately use anonymous types here so we don't depend on the ctor
            // signatures of the config records.
            var defaultConfigObject = new
            {
                Server = new
                {
                    BindAddress = "0.0.0.0",
                    Port = 2121,
                    PassivePortStart = 50000,
                    PassivePortEnd = 50100,
                    RootPath = defaultRootPath,
                    WelcomeMessage = "Welcome to amFTPd",
                    AllowAnonymous = false,
                    RequireTlsForAuth = false,

                    // Use the enum name so it round-trips cleanly with Enum.TryParse.
                    // Short RFC codes (C/P/S/E) are still supported by the parser below.
                    DataChannelProtectionDefault = "Clear",

                    AllowActiveMode = true,
                    AllowFxp = false
                },
                Tls = new
                {
                    PfxPath = defaultPfxPath,
                    PfxPassword = "",
                    SubjectName = "CN=amFTPd"
                },
                Storage = new
                {
                    UsersDbPath = defaultUsersDbPath,
                    SectionsPath = defaultSectionsPath,
                    UserStoreBackend = "binary",
                    MasterPassword = "changeme",
                    GroupsDbPath = defaultGroupsDbPath,
                    SectionsDbPath = defaultSectionsDbPath,
                    UseMmap = true
                },

                // ---------------------------------
                // VFS: basic root mount
                // ---------------------------------
                Vfs = new
                {
                    Mounts = new[]
        {
                        new
                        {
                            VirtualPath  = "/",              // exposed to FTP clients
                            PhysicalPath = defaultRootPath,  // actual FS path
                            IsReadOnly   = false
                        }
                    },
                    UserMounts = Array.Empty<object>()
                },

                // These stay empty by default; engines handle "no rules"
                Ident = new { },
                Sections = new { },  // Dictionary<string, SectionRule>
                DirectoryRules = new { },  // Dictionary<string, DirectoryRule>
                RatioRules = new { },  // Dictionary<string, RatioRule>
                Groups = new { },  // Dictionary<string, GroupConfig>
                FxpPolicy = (object?)null,
                Irc = (object?)null,
                
                Zipscript = new
                {
                    Enabled = true,
                    DatabasePath = string.Empty,
                    OffloadHeavyChecks = false,
                    StrictSfv = false,
                    IncludeSubdirsOnRescan = false
                },
                Status = new
                {
                    Enabled = false,
                    BindAddress = "127.0.0.1",
                    Port = 8080,
                    Path = "/amftpd-status/"
                },
                Compatibility = new
                {
                    Profile = "none",              // "none", "gl", "io", "raiden", ...
                    EnableCommandAliases = true,
                    GlStyleSiteStat = false,
                    IoStyleSiteWho = false,
                    IrcGlStyleMessages = false,
                    SiteCommandAliases = new { }   // reserved for future use
                }
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var defaultJson = JsonSerializer.Serialize(defaultConfigObject, jsonOptions);
            await File.WriteAllTextAsync(configPath, defaultJson).ConfigureAwait(false);

            logger.Log(FtpLogLevel.Info,
                $"Default configuration written to '{configPath}'. " +
                "Review and adjust it before using amFTPd in production.");
        }

        // ------------------------------------------
        // Load JSON config (file now exists)
        // ------------------------------------------
        var json = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);

        var root = JsonSerializer.Deserialize<AmFtpdConfigRoot>(json)
                   ?? throw new InvalidOperationException("Invalid configuration format.");

        // ------------------------------------------------------------------
        // Status / monitoring config (optional)
        // ------------------------------------------------------------------
        var statusConfig = root.Status ?? new AmFtpdStatusConfig(
            Enabled: false,
            BindAddress: "127.0.0.1",
            Port: 8080,
            Path: "/amftpd-status/"
        );

        // ------------------------------------------
        // Build FtpConfig
        // ------------------------------------------
        var ftpRoot = Path.GetFullPath(root.Server.RootPath);
        Directory.CreateDirectory(ftpRoot);

        // BindAddress: string -> IPAddress?
        var bindAddress = string.IsNullOrWhiteSpace(root.Server.BindAddress) ? null : root.Server.BindAddress;

        // Passive ports as tuple (Start, End)
        string? passivePorts = null; 
        if (root.Server.PassivePortStart > 0 && root.Server.PassivePortEnd >= root.Server.PassivePortStart)
            passivePorts = $"{root.Server.PassivePortStart}-{root.Server.PassivePortEnd}";

        // Parse data channel protection mode from string into enum.
        // Accept both enum names (Clear, Private, ...) and RFC short codes (C, S, E, P).
        var rawDataProt = (root.Server.DataChannelProtectionDefault ?? string.Empty).Trim();

        DataChannelProtectionLevel dataProt;

        if (string.IsNullOrEmpty(rawDataProt))
            dataProt = DataChannelProtectionLevel.Clear;
        else if (rawDataProt.Equals("C", StringComparison.OrdinalIgnoreCase))
            dataProt = DataChannelProtectionLevel.Clear;
        else if (rawDataProt.Equals("S", StringComparison.OrdinalIgnoreCase))
            dataProt = DataChannelProtectionLevel.Safe;
        else if (rawDataProt.Equals("E", StringComparison.OrdinalIgnoreCase))
            dataProt = DataChannelProtectionLevel.Confidential;
        else if (rawDataProt.Equals("P", StringComparison.OrdinalIgnoreCase))
            dataProt = DataChannelProtectionLevel.Private;
        else if (!Enum.TryParse<DataChannelProtectionLevel>(rawDataProt, ignoreCase: true, out dataProt))
        {
            logger.Log(
                FtpLogLevel.Warn,
                $"Invalid DataChannelProtectionDefault '{root.Server.DataChannelProtectionDefault}', using Clear.");
            dataProt = DataChannelProtectionLevel.Clear;
        }

        var ftpCfg = new FtpConfig(
            BindAddress: bindAddress,
            Port: root.Server.Port,
            PassivePorts: passivePorts,
            RootPath: ftpRoot,
            WelcomeMessage: root.Server.WelcomeMessage,
            AllowAnonymous: root.Server.AllowAnonymous,
            EnableExplicitTls: true,
            RequireTlsForAuth: root.Server.RequireTlsForAuth,
            DataChannelProtectionDefault: dataProt,
            AllowActiveMode: root.Server.AllowActiveMode,
            AllowFxp: root.Server.AllowFxp
        );

        // Apply compatibility profile if present in the root config.
        if (root.Compatibility is not null)
        {
            ftpCfg = ftpCfg with { Compatibility = root.Compatibility };
        }

        // ------------------------------------------
        // DatabaseManager (Users, Groups, Sections)
        // ------------------------------------------
        DatabaseManager? db = null;
        IUserStore userStore;
        IGroupStore? groupStore = null;
        ISectionStore? sectionStore = null;

        var backend = root.Storage.UserStoreBackend?.Trim() ?? "json";
        var useBinary =
            backend.Equals("binary", StringComparison.OrdinalIgnoreCase);

        if (useBinary)
            try
            {
                var dbBaseDir = Path.GetDirectoryName(root.Storage.UsersDbPath)
                                ?? throw new InvalidOperationException("Invalid UsersDbPath base directory.");

                db = DatabaseManager.Load(
                    baseDir: dbBaseDir,
                    masterPassword: root.Storage.MasterPassword,
                    useMmapForUsers: root.Storage.UseMmap,
                    debugLog: msg => logger.Log(FtpLogLevel.Debug, msg));

                userStore = db.Users;
                groupStore = db.Groups;
                sectionStore = db.Sections;
            }
            catch (Exception ex)
            {
                logger.Log(FtpLogLevel.Error,
                    $"Failed to initialize binary database backend: {ex.Message}");
                throw;
            }
        else
        {
            logger.Log(FtpLogLevel.Info,
                "Using JSON/config backend for users/groups/sections (UserStoreBackend != 'binary').");

            userStore = InMemoryUserStore.LoadFromFile(root.Storage.UsersDbPath);

            groupStore = null;
            sectionStore = null;
        }

        // Dupe store (file-based)
        IDupeStore? dupeStore;
        var usersDbPath = root.Storage.UsersDbPath;
        var dbBaseDir_ = Path.GetDirectoryName(usersDbPath);
        if (string.IsNullOrWhiteSpace(dbBaseDir_))
        {
            dbBaseDir_ = AppContext.BaseDirectory;
        }

        var dupePath = Path.Combine(dbBaseDir_!, "amftpd-dupe.json");
        dupeStore = new FileDupeStore(dupePath);

        // ------------------------------------------
        // SectionManager (runtime FtpSection model)
        // ------------------------------------------
        var sections =
            sectionStore is not null
                ? SectionManager.FromSectionStore(sectionStore, "db")
                : SectionManager.LoadOrCreateDefault(root.Storage.SectionsPath);

        // ------------------------------------------
        // TLS
        // ------------------------------------------
        var tlsCfg = await TlsConfig.CreateOrLoadAsync(
            root.Tls.PfxPath,
            root.Tls.PfxPassword,
            root.Tls.SubjectName,
            logger).ConfigureAwait(false);

        // ======================================================================
        // RATIO SYSTEM (SectionRule-based)
        // ======================================================================
        var dirEngine = new DirectoryRuleEngine(root.DirectoryRules);

        var ratioSectionResolver =
            new Ftpd.RatioRules.SectionResolver(root.Sections);

        var ratioPipeline = new RatioResolutionPipeline(
            dirEngine,
            ratioSectionResolver,
            root.RatioRules
        );

        var ratioEngine = new RatioEngine(
            root.Sections,
            root.DirectoryRules,
            root.RatioRules,
            root.Groups
        );

        var raceEngine = new RaceEngine();

        ZipscriptEngine? zipscript = null;
        var zCfg = root.Zipscript ?? new ZipscriptConfig();

        if (!zCfg.Enabled)
        {
            logger.Log(FtpLogLevel.Info, "Zipscript engine disabled via configuration.");
        }
        else
        {
            try
            {
                // Place zipscript DB next to the user DB by default.
                var dbBaseDir = Path.GetDirectoryName(root.Storage.UsersDbPath);
                if (string.IsNullOrWhiteSpace(dbBaseDir))
                    dbBaseDir = AppContext.BaseDirectory;

                var dbPath = string.IsNullOrWhiteSpace(zCfg.DatabasePath)
                    ? Path.Combine(dbBaseDir!, "amftpd-zipscript.db")
                    : Path.GetFullPath(zCfg.DatabasePath);

                var zDb = new ZipscriptDbContext(dbPath);
                zipscript = new ZipscriptEngine(zDb, logger, zCfg);

                logger.Log(FtpLogLevel.Info, $"Zipscript engine enabled. DB path: {dbPath}");
            }
            catch (Exception ex)
            {
                logger.Log(
                    FtpLogLevel.Warn,
                    $"Failed to initialize zipscript engine; running without it. {ex.Message}",
                    ex);

                // Fall back to in-memory-only engine (no persistence).
                zipscript = new ZipscriptEngine(null, logger, zCfg);
            }
        }

        FxpPolicyEngine? fxpPolicy = null;
        if (root.FxpPolicy is not null)
            fxpPolicy = new FxpPolicyEngine(root.FxpPolicy, tlsCfg);


        // ======================================================================
        // VFS SYSTEM (FtpSection-based)
        // ======================================================================

        var vfsSectionResolver =
            new Core.Sections.SectionResolver(sections.GetSections());

        // ======================================================================
        // CREDIT ENGINE
        // ======================================================================
        CreditEngine? creditEngine = null;

        if (groupStore is not null && sectionStore is not null)
            creditEngine = new CreditEngine(userStore, groupStore, sectionStore)
            {
                DebugLog = msg => logger.Log(FtpLogLevel.Debug, msg)
            };

        // ======================================================================
        // Return combined runtime configuration
        // ======================================================================

        return new AmFtpdRuntimeConfig
        {
            FtpConfig = ftpCfg,
            UserStore = userStore,
            Sections = sections,
            TlsConfig = tlsCfg,
            IdentConfig = root.Ident,
            VfsConfig = root.Vfs,
            Database = db,

            SectionRules = root.Sections,
            DirectoryRules = root.DirectoryRules,
            RatioRules = root.RatioRules,
            Groups = root.Groups,

            RatioEngine = ratioEngine,
            RatioPipeline = ratioPipeline,
            DirectoryRuleEngine = dirEngine,
            RaceEngine = raceEngine,

            SectionResolver = vfsSectionResolver,

            GroupStore = groupStore,
            SectionStore = sectionStore,
            Zipscript = zipscript,
            DupeStore = dupeStore,

            FxpPolicy = fxpPolicy,
            IrcConfig = root.Irc,

            CreditEngine = creditEngine,

            ConfigFilePath = configPath,
            RawJson = json,
            LoadedAtUtc = DateTimeOffset.UtcNow,
            StatusConfig = statusConfig
        };
    }

    public sealed record AmFtpdConfigReloadResult(
    AmFtpdRuntimeConfig Runtime,
    IReadOnlyList<string> ChangedSections);

    public static async Task<AmFtpdConfigReloadResult> ReloadAsync(
        string configPath,
        AmFtpdRuntimeConfig current,
        IFtpLogger logger,
        CancellationToken cancellationToken = default)
    {
        if (configPath is null) throw new ArgumentNullException(nameof(configPath));
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (logger is null) throw new ArgumentNullException(nameof(logger));

        logger.Log(FtpLogLevel.Info, $"Reloading configuration from '{configPath}'...");

        // Reuse existing loader to fully validate and build a new runtime snapshot.
        var newRuntime = await LoadAsync(configPath, logger).ConfigureAwait(false);

        var changedSections = new List<string>();

        try
        {
            using var oldDoc = JsonDocument.Parse(current.RawJson);
            using var newDoc = JsonDocument.Parse(newRuntime.RawJson);

            var oldRoot = oldDoc.RootElement;
            var newRoot = newDoc.RootElement;

            void Check(string jsonPropName, string humanName)
            {
                if (oldRoot.TryGetProperty(jsonPropName, out var o) &&
                    newRoot.TryGetProperty(jsonPropName, out var n))
                {
                    if (!o.Equals(n))
                        changedSections.Add(humanName);
                }
                else if (oldRoot.TryGetProperty(jsonPropName, out _) ||
                         newRoot.TryGetProperty(jsonPropName, out _))
                {
                    // Exists only in one document → treat as changed.
                    changedSections.Add(humanName);
                }
            }

            Check("Server", "server");
            Check("Tls", "tls");
            Check("Storage", "storage");
            Check("Ident", "ident");
            Check("Vfs", "vfs");
            Check("Sections", "sections");
            Check("DirectoryRules", "directory rules");
            Check("RatioRules", "ratio rules");
            Check("Groups", "groups");
            Check("FxpPolicy", "fxp policy");
            Check("Irc", "irc");
            Check("Zipscript", "zipscript");
        }
        catch (Exception ex)
        {
            logger.Log(
                FtpLogLevel.Warn,
                "Config reload diffing failed; configuration was reloaded but change summary may be incomplete.",
                ex);
        }

        return new AmFtpdConfigReloadResult(newRuntime, changedSections);
    }

    private static async Task EnsureDefaultConfigExistsAsync(
            string configPath,
            IFtpLogger log,
            CancellationToken ct)
    {
        if (File.Exists(configPath))
            return;

        var baseDir = AppContext.BaseDirectory;

        // Default root and DB paths
        var ftpRoot = Path.Combine(baseDir, "ftp-root");
        Directory.CreateDirectory(ftpRoot);

        var usersDbPath = Path.Combine(baseDir, "amftpd-users.db");
        var sectionsPath = Path.Combine(baseDir, "sections");
        var groupsDbPath = Path.Combine(baseDir, "amftpd-groups.db");
        var sectionsDbPath = Path.Combine(baseDir, "amftpd-sections.db");

        Directory.CreateDirectory(sectionsPath);

        log.Log(FtpLogLevel.Info,$"No config file found at '{configPath}'. Generating default configuration.");

        // We use an anonymous object here so we don't need constructors
        // for AmFtpdTlsConfig, IdentConfig, etc. Property names match
        // AmFtpdConfigRoot.
        var defaultModel = new
        {
            Server = new
            {
                BindAddress = "0.0.0.0",
                Port = 2121,
                PassivePortStart = 50000,
                PassivePortEnd = 50100,
                RootPath = ftpRoot,
                WelcomeMessage = "220 amFTPd ready.",
                AllowAnonymous = false,
                RequireTlsForAuth = false,
                DataChannelProtectionDefault = "C", // Clear
                AllowActiveMode = true,
                AllowFxp = false
            },

            // Leave TLS empty -> treated as "disabled" by the rest of the code.
            Tls = new { },

            Storage = new
            {
                UsersDbPath = usersDbPath,
                SectionsPath = sectionsPath,
                UserStoreBackend = "binary",              // or "json" if you prefer
                MasterPassword = "CHANGE_ME_MASTER",    // used for BinaryUserStore encryption
                GroupsDbPath = groupsDbPath,
                SectionsDbPath = sectionsDbPath,
                UseMmap = true
            },

            // Ident disabled by default
            Ident = new
            {
                Enabled = false,
                TimeoutSec = 5,
                StrictMatch = false
            },

            // Keep VFS minimal here – we’ll ensure defaults in code if needed.
            Vfs = new
            {
                // If your VfsConfig has other properties, you can extend this.
                Mounts = Array.Empty<object>(),
                UserMounts = Array.Empty<object>()
            },

            Sections = new { }, // empty object -> empty dictionary
            DirectoryRules = new { },
            RatioRules = new { },
            Groups = new { },

            FxpPolicy = (object?)null,
            Irc = (object?)null,

            Zipscript = new
            {
                Enabled = true,
                DatabasePath = string.Empty,       // use default path next to user DB
                OffloadHeavyChecks = false,
                StrictSfv = false,
                IncludeSubdirsOnRescan = false
            },
            Status = new
            {
                Enabled = false,
                BindAddress = "127.0.0.1",
                Port = 8080,
                Path = "/amftpd-status/"
            },
            Compatibility = new
            {
                Profile = "none",              // "none", "gl", "io", "raiden", ...
                EnableCommandAliases = true,
                GlStyleSiteStat = false,
                IoStyleSiteWho = false,
                IrcGlStyleMessages = false,
                SiteCommandAliases = new { }   // reserved for future use
            }
        };

        var json = JsonSerializer.Serialize(
            defaultModel,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, json, Encoding.UTF8, ct);

        log.Log(FtpLogLevel.Info,$"Default configuration written to '{configPath}'. Edit it and restart amFTPd if needed.");
    }
}