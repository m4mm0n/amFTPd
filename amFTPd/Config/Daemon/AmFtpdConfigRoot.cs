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
using amFTPd.Config.Ftpd.RatioRules;
using amFTPd.Config.Ident;
using amFTPd.Config.Vfs;

namespace amFTPd.Config.Daemon;

/// <summary>
/// Represents the root configuration for an AmFtpd server instance, including server, TLS, storage, identity, virtual
/// file system, and rule/group settings.
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
public sealed record AmFtpdConfigRoot(
    AmFtpdServerConfig Server,
    AmFtpdTlsConfig Tls,
    AmFtpdStorageConfig Storage,
    IdentConfig Ident,
    VfsConfig Vfs,

    // NEW — Section rules
    Dictionary<string, SectionRule> Sections,

    // NEW — Directory-level rules
    Dictionary<string, DirectoryRule> DirectoryRules,

    // NEW — Ratio rules (per-path or per-pattern)
    Dictionary<string, RatioRule> RatioRules,

    // NEW — Group configurations
    Dictionary<string, GroupConfig> Groups
);
