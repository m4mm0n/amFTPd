/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdRuntimeConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-28 22:06:08
 *  Last Modified:  2025-12-14 17:15:14
 *  CRC32:          0x8E76E041
 *  
 *  Description:
 *      Represents the runtime configuration for the amFTPd daemon.
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
using amFTPd.Config.Ftpd.RatioRules;
using amFTPd.Config.Ident;
using amFTPd.Config.Irc;
using amFTPd.Config.Vfs;
using amFTPd.Core.Affils;
using amFTPd.Core.AutoNuke;
using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Fxp;
using amFTPd.Core.Logging;
using amFTPd.Core.Messages;
using amFTPd.Core.Oneliners;
using amFTPd.Core.Pre;
using amFTPd.Core.Quota;
using amFTPd.Core.Race;
using amFTPd.Core.Ratio;
using amFTPd.Core.ReleaseSystem;
using amFTPd.Core.Requests;
using amFTPd.Core.Runtime;
using amFTPd.Core.Stats;
using amFTPd.Core.Stats.Live;
using amFTPd.Core.Stats.Rolling;
using amFTPd.Core.Vfs;
using amFTPd.Core.Zipscript;
using amFTPd.Credits;
using amFTPd.Db;
using amFTPd.Logging;
using amFTPd.Scripting.Tcl;
using amFTPd.Security;
using SectionResolver = amFTPd.Core.Sections.SectionResolver;

namespace amFTPd.Config.Daemon
{
    /// <summary>
    /// Represents the runtime configuration for the amFTPd daemon.
    /// </summary>
    /// <remarks>
    /// This class encapsulates the configuration required for the FTP server, 
    /// including FTP settings, user store, section management, and TLS configuration.
    /// </remarks>
    public sealed class AmFtpdRuntimeConfig
    {
        /// <summary>
        /// Gets the FTP configuration settings required to connect to the FTP server.
        /// </summary>
        public required FtpConfig FtpConfig { get; init; }
        /// <summary>
        /// Gets the user store configuration for the FTP daemon.
        /// </summary>
        /// <remarks>
        /// This property provides access to the <see cref="IUserStore"/> implementation, 
        /// which is responsible for managing user-related data and operations within the FTP server.
        /// </remarks>
        public required IUserStore UserStore { get; init; }
        /// <summary>
        /// Gets the <see cref="SectionManager"/> instance responsible for managing
        /// configuration sections within the FTP daemon runtime.
        /// </summary>
        /// <remarks>
        /// This property is required and must be initialized during the creation of
        /// the <see cref="AmFtpdRuntimeConfig"/> instance. It provides access to
        /// the configuration sections used by the FTP server.
        /// </remarks>
        public required SectionManager Sections { get; init; }
        /// <summary>
        /// Gets or sets the TLS configuration settings used for secure network communication.
        /// </summary>
        public required TlsConfig TlsConfig { get; init; }
        /// <summary>
        /// Gets the identification configuration for the FTP daemon.
        /// </summary>
        public required IdentConfig IdentConfig { get; init; }
        /// <summary>
        /// QuickLog daemon logging configuration.
        /// </summary>
        public required QuickLogOptions Logging { get; init; }
        /// <summary>
        /// Gets the configuration settings for the virtual file system.
        /// </summary>
        public required VfsConfig VfsConfig { get; init; }
        /// <summary>
        /// Gets the database manager associated with the current instance.
        /// </summary>
        public DatabaseManager? Database { get; init; }
        /// <summary>
        /// Gets the collection of rules that apply to each section, keyed by section name.
        /// </summary>
        public required Dictionary<string, SectionRule> SectionRules { get; init; }
        /// <summary>
        /// Gets the collection of directory rules, keyed by directory path.
        /// </summary>
        public required Dictionary<string, DirectoryRule> DirectoryRules { get; init; }
        /// <summary>
        /// Gets the collection of ratio rules used to determine allocation or distribution logic.
        /// </summary>
        public required Dictionary<string, RatioRule> RatioRules { get; init; }
        /// <summary>
        /// Gets the collection of group configurations, keyed by group name.
        /// </summary>
        public required Dictionary<string, GroupConfig> Groups { get; init; }
        private readonly object _groupsSync = new();
        /// <summary>
        /// Gets the ratio engine responsible for managing user ratios.
        /// </summary>
        public required RatioEngine RatioEngine { get; init; }
        /// <summary>
        /// Gets the ratio resolution pipeline used to process ratio calculations.
        /// </summary>
        public required RatioResolutionPipeline RatioPipeline { get; init; }
        /// <summary>
        /// Gets the directory rule engine responsible for applying directory rules.
        /// </summary>
        public required DirectoryRuleEngine DirectoryRuleEngine { get; init; }
        /// <summary>
        /// Gets the race engine used to manage and execute race logic.
        /// </summary>
        public required RaceEngine RaceEngine { get; init; }
        /// <summary>
        /// Gets or sets the delegate used to resolve configuration sections.
        /// </summary>
        public required SectionResolver SectionResolver { get; init; }
        /// <summary>
        /// Gets the group store used to manage and retrieve group-related data.
        /// </summary>
        public IGroupStore? GroupStore { get; init; }
        /// <summary>
        /// Gets the section store used to manage configuration sections.
        /// </summary>
        public ISectionStore? SectionStore { get; init; }
        /// <summary>
        /// Gets the dupe store used to manage duplicate file detection and handling.
        /// </summary>
        public IDupeStore? DupeStore { get; init; }
        /// <summary>
        /// Gets the zipscript engine used for processing zipscript commands.
        /// </summary>
        public ZipscriptEngine? Zipscript { get; init; }
        /// <summary>
        /// Gets the event bus for publishing and subscribing to events within the FTP daemon.
        /// </summary>
        public EventBus EventBus { get; init; } = new();
        /// <summary>
        /// Gets the FXP policy engine used to manage FXP transfer policies.
        /// </summary>
        public FxpPolicyEngine? FxpPolicy { get; init; }
        /// <summary>
        /// Gets the IRC configuration for the FTP daemon.
        /// </summary>
        public IrcConfig? IrcConfig { get; init; }
        /// <summary>
        /// Gets the status/monitoring configuration for the HTTP status endpoint.
        /// </summary>
        public AmFtpdStatusConfig? StatusConfig { get; init; }
        /// <summary>
        /// Gets the credit engine used for managing user credits.
        /// </summary>
        public CreditEngine? CreditEngine { get; init; }
        /// <summary>
        /// Full path to the JSON configuration file this runtime was built from.
        /// </summary>
        public required string ConfigFilePath { get; init; }
        /// <summary>
        /// Raw JSON payload that produced this runtime snapshot.
        /// Used for coarse diffing on reload.
        /// </summary>
        public required string RawJson { get; init; }
        /// <summary>
        /// Timestamp (UTC) when this runtime snapshot was constructed.
        /// </summary>
        public DateTimeOffset LoadedAtUtc { get; init; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Returns a snapshot copy of all groups for thread-safe read access.
        /// </summary>
        public Dictionary<string, GroupConfig> GetGroupsSnapshot()
        {
            lock (_groupsSync)
            {
                return new Dictionary<string, GroupConfig>(Groups, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Adds or replaces a runtime group entry.
        /// </summary>
        public void SetGroup(string groupName, GroupConfig cfg)
        {
            lock (_groupsSync)
            {
                Groups[groupName] = cfg;
            }
        }

        /// <summary>
        /// Removes a runtime group entry.
        /// </summary>
        public bool RemoveGroup(string groupName)
        {
            lock (_groupsSync)
            {
                return Groups.Remove(groupName);
            }
        }

        /// <summary>
        /// Tries to read a runtime group config with shared locking.
        /// </summary>
        public bool TryGetGroup(string groupName, out GroupConfig? cfg)
        {
            lock (_groupsSync)
            {
                return Groups.TryGetValue(groupName, out cfg);
            }
        }

        /// <summary>
        /// Checks if a runtime group exists.
        /// </summary>
        public bool ContainsGroup(string groupName)
        {
            lock (_groupsSync)
            {
                return Groups.ContainsKey(groupName);
            }
        }
        /// <summary>
        /// Gets the statistics collector used to gather and report runtime metrics for this instance.
        /// </summary>
        public StatsCollector StatsCollector { get; init; } =
            new StatsCollector(TimeSpan.FromSeconds(1));
        /// <summary>
        /// Gets the registry that provides access to live application statistics.
        /// </summary>
        public LiveStatsRegistry LiveStats { get; } = new();
        /// <summary>
        /// Gets the rolling statistical calculations for the current data set.
        /// </summary>
        public RollingStats RollingStats { get; } = new();
        /// <summary>
        /// Registry backing the virtual /PRE hierarchy.
        /// </summary>
        public PreRegistry PreRegistry { get; } = new();

        /// <summary>
        /// Shoutbox (oneliner) store. Null if not initialised (config dir unknown at runtime build time).
        /// Initialised lazily by FtpServer after the config path is known.
        /// </summary>
        public OnelineStore? OnelineStore { get; set; }

        /// <summary>
        /// Request registry. Null if not initialised.
        /// Initialised lazily by FtpServer after the config path is known.
        /// </summary>
        public RequestRegistry? RequestRegistry { get; set; }

        /// <summary>
        /// Affil store (section → affiliated groups). Null if not initialised.
        /// Initialised lazily by FtpServer after the config path is known.
        /// </summary>
        public AffilStore? AffilStore { get; set; }

        /// <summary>
        /// Message engine for MOTD, login messages, and per-directory .message files.
        /// Null until initialised by FtpServer.
        /// </summary>
        public MessageEngine? Messages { get; set; }

        /// <summary>
        /// Auto-nuke rules evaluated against zipscript completion/update events.
        /// Empty by default (no automatic nukes). Populated from JSON config.
        /// </summary>
        public IReadOnlyList<AutoNukeRule> AutoNukeRules { get; init; } =
            Array.Empty<AutoNukeRule>();
        /// <summary>
        /// Time-to-live for PRE entries.
        /// Default: 48 hours.
        /// </summary>
        public TimeSpan PreTtl { get; init; } = TimeSpan.FromHours(48);
        /// <summary>
        /// Coordinates startup recovery and persistence.
        /// </summary>
        public RuntimeRecoveryManager Recovery { get; internal set; } = null!;
        /// <summary>
        /// True while the daemon is restoring persistent state.
        /// </summary>
        public bool IsRecovering => Recovery?.IsRecovering ?? false;
        /// <summary>
        /// wu-ftpd-compatible xferlog writer. Null until initialised by FtpServer.
        /// Subscribes to EventBus Upload/Download events.
        /// </summary>
        public XferlogWriter? Xferlog { get; set; }

        /// <summary>
        /// Structured admin mutation audit log (JSONL). Null until initialised by FtpServer.
        /// Call <c>runtime.AuditLog?.Log(...)</c> from any SITE command that mutates state.
        /// </summary>
        public AuditLogWriter? AuditLog { get; set; }

        /// <summary>
        /// Per-user upload quota tracker (daily / weekly / monthly).
        /// Initialised by FtpServer and subscribed to EventBus Upload events.
        /// Check with <c>UploadQuota?.Check(user, Groups)</c> before accepting STOR.
        /// </summary>
        public UploadQuotaStore? UploadQuota { get; set; }

        /// <summary>
        /// Plugin host that manages all loaded extension plugins.
        /// Null until after <see cref="amFTPd.Config.Daemon.AmFtpdConfigLoader"/> loads plugins.
        /// </summary>
        public amFTPd.Core.Plugins.PluginHost? PluginHost { get; set; }

        /// <summary>
        /// Virtual symlink registry. Maps link virtual paths to target virtual paths.
        /// Null until initialised by FtpServer after the config path is known.
        /// Passed to each session's VfsManager so symlinks are resolved transparently.
        /// </summary>
        public VfsSymlinkStore? SymlinkStore { get; set; }

        /// <summary>
        /// Gets the registry that provides access to available releases.
        /// </summary>
        public ReleaseRegistry ReleaseRegistry { get; } = new();

        /// <summary>
        /// Outbound HTTP webhook configuration.
        /// When non-null and Enabled, <see cref="amFTPd.Core.Webhooks.WebhookDispatcher"/>
        /// fires HTTP POSTs on configured event types.
        /// </summary>
        public AmFtpdWebhookConfig? WebhookConfig { get; init; }

        /// <summary>
        /// ACME v2 automatic TLS certificate configuration.
        /// When non-null and Enabled, <see cref="amFTPd.Core.Tls.AcmeCertificateManager"/>
        /// provisions and renews the certificate from the configured CA.
        /// </summary>
        public AmFtpdAcmeConfig? AcmeConfig { get; init; }

        /// <summary>
        /// Absolute path to the PFX certificate file used for TLS.
        /// Stored here so that <see cref="amFTPd.Core.Tls.AcmeCertificateManager"/> knows
        /// where to write renewed certificates without re-parsing the raw JSON config.
        /// </summary>
        public string? TlsPfxPath { get; init; }

        /// <summary>
        /// Password protecting the PFX file at <see cref="TlsPfxPath"/>.
        /// May be null or empty for unencrypted PFX files.
        /// </summary>
        public string? TlsPfxPassword { get; init; }

        /// <summary>
        /// Configuration for glFTPd-compatible TCL scripts.
        /// </summary>
        public TclConfig? Tcl { get; init; }

        /// <summary>
        /// Runner for glFTPd-compatible TCL scripts.
        /// Initialised by FtpServer.
        /// </summary>
        public GlftpdTclRunner? TclRunner { get; set; }
    }
}
