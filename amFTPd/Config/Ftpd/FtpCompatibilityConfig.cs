/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpCompatibilityConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 17:50:40
 *  Last Modified:  2025-12-14 17:56:57
 *  CRC32:          0xED10BC7B
 *  
 *  Description:
 *      Compatibility settings for command syntax, human-facing output and IRC messages.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Compatibility settings for command syntax, human-facing output and IRC messages.
    /// </summary>
    /// <param name="Profile">
    /// A short label describing the compatibility profile, e.g. "none", "gl", "io", "raiden".
    /// </param>
    /// <param name="EnableCommandAliases">
    /// Whether SITE command aliases should be resolved (gl/io style verbs mapped to amFTPd verbs).
    /// </param>
    /// <param name="GlStyleSiteStat">
    /// If true, SITE STATS output will prefer a gl-like layout where practical.
    /// </param>
    /// <param name="IoStyleSiteWho">
    /// If true, SITE WHO output layout will approximate ioFTPD-style listing.
    /// </param>
    /// <param name="IrcGlStyleMessages">
    /// If true, IRC messages will default to gl/io style templates where configured.
    /// </param>
    /// <param name="SiteCommandAliases">
    /// If provided, a mapping of SITE command aliases to canonical command names.
    /// </param>
    public sealed record FtpCompatibilityConfig(
        string Profile = "none",
        bool EnableCommandAliases = true,
        bool GlStyleSiteStat = false,
        bool IoStyleSiteWho = false,
        bool IrcGlStyleMessages = false,
        IDictionary<string, string> SiteCommandAliases = null // alias -> canonical
    );
}
