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

namespace amFTPd.Config.Daemon;

/// <summary>
/// Represents the root configuration for the amFTPd, encompassing server, TLS, and storage settings.
/// </summary>
/// <param name="Server">The configuration settings for the FTP server, including host, port, and other server-specific options.</param>
/// <param name="Tls">The configuration settings for TLS, specifying certificates and security protocols for secure communication.</param>
/// <param name="Storage">The configuration settings for storage, defining paths, quotas, and other storage-related options.</param>
public sealed record AmFtpdConfigRoot(
    AmFtpdServerConfig Server,
    AmFtpdTlsConfig Tls,
    AmFtpdStorageConfig Storage
);