/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdStorageConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 20:53:20
 *  CRC32:          0x4E332AD2
 *  
 *  Description:
 *      Represents the configuration settings for the AmFtpd storage system.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


namespace amFTPd.Config.Daemon;

/// <summary>
/// Represents the configuration settings for the AmFtpd storage system.
/// </summary>
/// <param name="UsersDbPath">The file path to the database containing user information. This path must point to a valid file.</param>
/// <param name="SectionsPath">The directory path where section data is stored. This path must exist and be accessible.</param>
/// <param name="UserStoreBackend">Specifies the backend format for the user store. Valid values are <see langword="json"/> or <see
/// langword="binary"/>.</param>
/// <param name="MasterPassword">The master password used for encrypting the BinaryUserStore. This value is required if <paramref
/// name="UserStoreBackend"/> is set to <see langword="binary"/>.</param>
/// <param name="GroupsDbPath">The file path to the database containing group information. Defaults to "amftpd-groups.db" if not specified.</param>
/// <param name="SectionsDbPath">The file path to the database containing section information. Defaults to "amftpd-sections.db" if not specified.</param>
/// <param name="UseMmap">A value indicating whether memory-mapped files should be used for database operations. Defaults to <see
/// langword="true"/>.</param>
public sealed record AmFtpdStorageConfig(
    string UsersDbPath,
    string SectionsPath,
    string UserStoreBackend, // "json" or "binary"
    string MasterPassword,    // used for BinaryUserStore encryption
    string GroupsDbPath = "amftpd-groups.db",
    string SectionsDbPath = "amftpd-sections.db",
    bool UseMmap = true
);