/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03
 *  Last Modified:  2025-12-03
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

namespace amFTPd.Config.Fxp;

/// <summary>
/// Static FXP policy configuration.
/// Later you can bind this from JSON config if you want.
/// </summary>
public sealed class FxpPolicyConfig
{
    /// <summary>Globally enable/disable FXP handling.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Allow incoming FXP (remote -> here) when Enabled is true.</summary>
    public bool AllowIncoming { get; init; } = false;

    /// <summary>Allow outgoing FXP (here -> remote) when Enabled is true.</summary>
    public bool AllowOutgoing { get; init; } = false;

    /// <summary>
    /// If true, admins bypass most FXP restrictions (except maybe hard-denied hosts/sections).
    /// </summary>
    public bool AdminsBypass { get; init; } = true;

    /// <summary>
    /// If true, a user must have AllowFxp=true on their account to use FXP.
    /// </summary>
    public bool RequireUserAllowFlag { get; init; } = true;

    /// <summary>
    /// If true, FXP to/from the same IP/host is forbidden (to avoid loop / bounce abuse).
    /// </summary>
    public bool DenySameHost { get; init; } = true;

    /// <summary>
    /// Sections where FXP is forbidden (names, case-insensitive).
    /// </summary>
    public ISet<string> DenySections { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional allowlist of sections where FXP is permitted.
    /// If non-empty, FXP is denied in any section not listed here.
    /// </summary>
    public ISet<string> AllowSections { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional list of trusted remote hosts (pattern or exact).
    /// If non-empty, FXP is denied to remote hosts not matching any entry.
    /// </summary>
    public ISet<string> TrustedHosts { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// If true, FXP is only allowed when remote ident/username matches the local user.
    /// (Best-effort, depends on how much info you have.)
    /// </summary>
    public bool RequireMatchingIdentForUser { get; init; } = false;
}