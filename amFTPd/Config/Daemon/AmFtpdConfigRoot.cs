/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdConfigRoot.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xCCF0AE50
 *  
 *  Description:
 *      Represents the root configuration for an AmFtpd server instance, including server, TLS, storage, identity, virtual fi...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using amFTPd.Config.Ftpd;
using amFTPd.Config.Ftpd.RatioRules;
using amFTPd.Config.Fxp;
using amFTPd.Config.Ident;
using amFTPd.Config.Irc;
using amFTPd.Config.Vfs;

namespace amFTPd.Config.Daemon;

/// <summary>
/// Represents the root configuration for an AmFtpd server instance, including server, TLS, storage, identity, virtual
/// file system, and various rule configurations.
/// </summary>
/// <param name="Server">The server configuration settings, including network endpoints and general server options.</param>
/// <param name="Tls">The TLS configuration settings used to secure FTP connections.</param>
/// <param name="Storage">The storage configuration specifying data directories and file handling options.</param>
/// <param name="Ident">The identity configuration for user identification and authentication.</param>
/// <param name="Vfs">The virtual file system configuration defining accessible paths and permissions.</param>
/// <param name="Sections">A collection of section rules, keyed by section name, that control configuration behavior for specific sections.</param>
/// <param name="DirectoryRules">A collection of directory-level rules, keyed by directory path, that define access and operational policies for
/// directories.</param>
/// <param name="RatioRules">A collection of ratio rules, keyed by path or pattern, that specify upload/download ratio requirements.</param>
/// <param name="Groups">A collection of group configurations, keyed by group name, that define user group settings and permissions.</param>
/// <param name="FxpPolicy">Optional FXP policy configuration that governs file transfer policies between FTP servers.</param>
/// <param name="Irc">Optional IRC configuration for integrating with IRC networks for notifications or commands.</param>
public sealed record AmFtpdConfigRoot(
    AmFtpdServerConfig Server,
    AmFtpdTlsConfig Tls,
    AmFtpdStorageConfig Storage,
    IdentConfig Ident,
    VfsConfig Vfs,
    Dictionary<string, SectionRule> Sections,
    Dictionary<string, DirectoryRule> DirectoryRules,
    Dictionary<string, RatioRule> RatioRules,
    Dictionary<string, GroupConfig> Groups,
    FxpPolicyConfig? FxpPolicy = null,
    IrcConfig? Irc = null
);
