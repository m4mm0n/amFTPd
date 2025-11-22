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
using amFTPd.Config.Ident;
using amFTPd.Config.Vfs;
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
    }
}
