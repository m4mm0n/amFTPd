/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-22
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

using amFTPd.Config.Ftpd;
using amFTPd.Config.Ftpd.RatioRules;
using amFTPd.Config.Ident;
using amFTPd.Config.Vfs;
using amFTPd.Core.Race;
using amFTPd.Core.Ratio;
using amFTPd.Security;

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
        /// Gets the configuration settings for the FTP connection.
        /// </summary>
        public required FtpConfig FtpConfig { get; init; }
        /// <summary>
        /// Gets the user store used to manage and persist user data.
        /// </summary>
        public required IUserStore UserStore { get; init; }
        /// <summary>
        /// Gets the <see cref="SectionManager"/> instance used to manage and access FTP section within the FTP server.
        /// </summary>
        public required SectionManager Sections { get; init; }
        /// <summary>
        /// Gets the TLS configuration settings required for secure communication.
        /// </summary>
        public required TlsConfig TlsConfig { get; init; }
        /// <summary>
        /// Gets the configuration settings for identity management.
        /// </summary>
        public required IdentConfig IdentConfig { get; init; }
        /// <summary>
        /// Gets the configuration settings for the virtual file system (VFS).
        /// </summary>
        public required VfsConfig VfsConfig { get; init; }
        /// <summary>
        /// Gets the configured section-level ratio rules.
        /// Key = section virtual root (e.g. "/movies")
        /// </summary>
        public required Dictionary<string, SectionRule> SectionRules { get; init; }

        /// <summary>
        /// Gets the configured directory-level rules.
        /// Key = directory virtual path (e.g. "/incoming/group")
        /// </summary>
        public required Dictionary<string, DirectoryRule> DirectoryRules { get; init; }

        /// <summary>
        /// Gets the configured per-path ratio rules.
        /// Key = virtual path or prefix (e.g. "/movies")
        /// </summary>
        public required Dictionary<string, RatioRule> RatioRules { get; init; }

        /// <summary>
        /// Gets group configuration data (per-group behavior),
        /// including ratio multipliers, bonus behavior, etc.
        /// </summary>
        public required Dictionary<string, GroupConfig> Groups { get; init; }

        /// <summary>
        /// Gets the ratio engine used for calculating and enforcing upload/download ratios.
        /// </summary>
        public required RatioEngine RatioEngine { get; init; }

        /// <summary>
        /// Gets the ratio pipeline used to determine ratio adjustments.
        /// </summary>
        public required RatioResolutionPipeline RatioPipeline { get; init; }

        /// <summary>
        /// Gets the rule engine used to evaluate directory-based rules for this instance.
        /// </summary>
        public required DirectoryRuleEngine DirectoryRuleEngine { get; init; }

        /// <summary>
        /// Gets the section resolver used to map virtual paths to sections.
        /// </summary>
        public required SectionResolver SectionResolver { get; init; }

        /// <summary>
        /// Gets the race engine used to record per-directory upload contributions for race stats and nukes.
        /// </summary>
        public required RaceEngine RaceEngine { get; init; }

    }
}
