/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteCommandContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-24 23:59:17
 *  Last Modified:  2025-12-11 04:10:15
 *  CRC32:          0xFF397B81
 *  
 *  Description:
 *      Context passed to all SITE commands.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Race;
using amFTPd.Credits;
using amFTPd.Db;
using amFTPd.Logging;

namespace amFTPd.Core.Site
{
    /// <summary>
    /// Context passed to all SITE commands.
    /// </summary>
    public sealed class SiteCommandContext
    {
        /// <summary>
        /// Gets the <see cref="FtpCommandRouter"/> instance responsible for routing FTP commands.
        /// </summary>
        public FtpCommandRouter Router { get; }

        /// <summary>
        /// Gets the current FTP session associated with the router.
        /// </summary>
        public FtpSession Session => Router.Session;

        /// <summary>
        /// Gets the logger instance used to log FTP-related operations and events.
        /// </summary>
        public IFtpLogger Log => Router.Log;

        /// <summary>
        /// Gets the user store associated with the current router.
        /// </summary>
        /// <remarks>
        /// The returned <see cref="IUserStore"/> is managed by the router and may depend on the
        /// router's configuration. Ensure the router is properly initialized before accessing this property.
        /// </remarks>
        public IUserStore Users => Router.Users;

        /// <summary>
        /// Gets the store that provides access to groups managed by the router.
        /// </summary>
        public IGroupStore Groups => Router.Groups;

        /// <summary>
        /// Gets the <see cref="SectionManager"/> instance that manages the sections of the server.
        /// </summary>
        public SectionManager Sections => Router.Sections;

        /// <summary>
        /// Gets the runtime configuration for the FTP server.
        /// </summary>
        public AmFtpdRuntimeConfig Runtime => Router.Runtime;

        /// <summary>
        /// Gets the current instance of the <see cref="DatabaseManager"/> associated with the runtime.
        /// </summary>
        public DatabaseManager? Database => Router.Runtime.Database;

        /// <summary>
        /// Gets the <see cref="CreditEngine"/> instance associated with the current router.
        /// </summary>
        public CreditEngine Credits => Router.Credits;

        /// <summary>
        /// Gets the <see cref="RaceEngine"/> instance associated with the current router.
        /// </summary>
        public RaceEngine RaceEngine => Router.RaceEngine;

        /// <summary>
        /// The SITE sub-command verb (e.g. "KICK", "BAN", "WHO").
        /// </summary>
        public string Verb { get; }

        /// <summary>
        /// The raw argument string after the SITE verb.
        /// </summary>
        public string Arguments { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SiteCommandContext"/> class with the specified FTP command
        /// router.
        /// </summary>
        /// <param name="router">The <see cref="FtpCommandRouter"/> instance used to route FTP commands. Cannot be <see langword="null"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="router"/> is <see langword="null"/>.</exception>
        public SiteCommandContext(FtpCommandRouter router)
            : this(router, string.Empty, string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SiteCommandContext"/> class with router and SITE verb/arguments.
        /// </summary>
        /// <param name="router">The <see cref="FtpCommandRouter"/> instance used to route FTP commands.</param>
        /// <param name="verb">SITE sub-command verb.</param>
        /// <param name="arguments">SITE arguments (after the verb).</param>
        public SiteCommandContext(FtpCommandRouter router, string verb, string arguments)
        {
            Router = router ?? throw new ArgumentNullException(nameof(router));
            Verb = verb ?? string.Empty;
            Arguments = arguments ?? string.Empty;
        }
    }

}
