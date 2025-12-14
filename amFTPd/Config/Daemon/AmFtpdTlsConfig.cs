/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdTlsConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 20:53:31
 *  CRC32:          0x8EE14FB8
 *  
 *  Description:
 *      Represents the configuration settings for TLS (Transport Layer Security) in an FTP server.
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
/// Represents the configuration settings for TLS (Transport Layer Security) in an FTP server.
/// </summary>
/// <param name="PfxPath">The file path to the PFX certificate used for TLS encryption. This must be a valid path to a PFX file.</param>
/// <param name="PfxPassword">The password required to access the PFX certificate. This value cannot be null or empty.</param>
/// <param name="SubjectName">The subject name of the certificate to be used for TLS. This is typically the distinguished name (DN) of the
/// certificate.</param>
public sealed record AmFtpdTlsConfig(
    string PfxPath,
    string PfxPassword,
    string SubjectName
);