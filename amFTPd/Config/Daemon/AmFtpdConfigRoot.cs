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

using amFTPd.Config.Ident;
using amFTPd.Config.Vfs;

namespace amFTPd.Config.Daemon;

/// <summary>
/// Represents the root configuration for the AM FTP daemon, encompassing server, TLS, storage, identification, and
/// virtual file system settings.
/// </summary>
/// <remarks>This record serves as a centralized container for all configuration sections required to initialize
/// and operate the AM FTP daemon. Each property corresponds to a specific aspect of the daemon's configuration,
/// allowing for modular and organized configuration management.</remarks>
/// <param name="Server">The configuration settings for the FTP server, including port and connection options.</param>
/// <param name="Tls">The configuration settings for TLS, specifying certificates and encryption options.</param>
/// <param name="Storage">The configuration settings for storage, defining file system paths and quotas.</param>
/// <param name="Ident">The configuration settings for user identification and authentication.</param>
/// <param name="Vfs">The configuration settings for the virtual file system, mapping logical paths to physical storage.</param>
public sealed record AmFtpdConfigRoot(
    AmFtpdServerConfig Server,
    AmFtpdTlsConfig Tls,
    AmFtpdStorageConfig Storage,
    IdentConfig Ident,
    VfsConfig Vfs
);