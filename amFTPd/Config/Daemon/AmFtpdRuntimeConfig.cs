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
using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Fxp;
using amFTPd.Core.Pre;
using amFTPd.Core.Race;
using amFTPd.Core.Ratio;
using amFTPd.Core.ReleaseSystem;
using amFTPd.Core.Runtime;
using amFTPd.Core.Stats;
using amFTPd.Core.Stats.Live;
using amFTPd.Core.Stats.Rolling;
using amFTPd.Core.Zipscript;
using amFTPd.Credits;
using amFTPd.Db;
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
        /// Gets the registry that provides access to available releases.
        /// </summary>
        public ReleaseRegistry ReleaseRegistry { get; } = new();
    }
}
