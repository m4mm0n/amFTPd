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
/// Represents the configuration settings for the AM FTPD storage system.
/// </summary>
/// <param name="UsersDbPath">The file path to the database containing user information.</param>
/// <param name="SectionsPath">The directory path where section data is stored.</param>
/// <param name="UserStoreBackend">Specifies the backend format for the user store. Valid values are <c>"json"</c> or <c>"binary"</c>.</param>
/// <param name="MasterPassword">The master password used for encrypting the user store when the <paramref name="UserStoreBackend"/> is set to
/// <c>"binary"</c>.</param>
public sealed record AmFtpdStorageConfig(
    string UsersDbPath,
    string SectionsPath,
    string UserStoreBackend, // "json" or "binary"
    string MasterPassword    // used for BinaryUserStore encryption
);