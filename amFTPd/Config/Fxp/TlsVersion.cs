/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           TlsVersion.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-13 21:09:06
 *  Last Modified:  2025-12-13 21:09:06
 *  CRC32:          0x1E19655D
 *  
 *  Description:
 *      Logical TLS version enum decoupled from concrete SslProtocols values.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Config.Fxp;

/// <summary>
/// Logical TLS version enum decoupled from concrete SslProtocols values.
/// </summary>
public enum TlsVersion
{
    Any = 0,
    Tls10 = 10,
    Tls11 = 11,
    Tls12 = 12,
    Tls13 = 13,
}