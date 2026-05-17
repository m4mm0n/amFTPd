/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdPluginEntry.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      JSON-deserialisable configuration for a single plugin entry in the
 *      "Plugins" array of amftpd.json.
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
/// Describes a single plugin to load, as written in the <c>"Plugins"</c> array
/// of <c>amftpd.json</c>.
/// </summary>
/// <example>
/// <code>
/// "Plugins": [
///   {
///     "Path": "plugins/MyAuth/MyAuth.dll",
///     "Enabled": true,
///     "Settings": {
///       "LdapServer": "ldap://corp.example.com",
///       "BaseDn":     "dc=corp,dc=example,dc=com"
///     }
///   }
/// ]
/// </code>
/// </example>
/// <param name="Path">
/// Path to the plugin DLL.
/// Relative paths are resolved from the directory containing <c>amftpd.json</c>.
/// The directory must also contain a <c>.deps.json</c> file (produced by
/// <c>dotnet publish</c>) so that the plugin's own NuGet dependencies can be loaded.
/// </param>
/// <param name="Enabled">
/// Set to <c>false</c> to skip loading the plugin without removing the entry.
/// Defaults to <c>true</c>.
/// </param>
/// <param name="Settings">
/// Arbitrary key-value settings passed verbatim to the plugin via
/// <see cref="amFTPd.Plugin.Abstractions.IPluginContext.Settings"/>.
/// </param>
public sealed record AmFtpdPluginEntry(
    string Path,
    bool Enabled = true,
    Dictionary<string, string>? Settings = null
);
