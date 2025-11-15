using amFTPd.Config.Ftpd;
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
    }
}
