/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpPolicyConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:58:36
 *  Last Modified:  2025-12-13 22:10:16
 *  CRC32:          0x3C8E6182
 *  
 *  Description:
 *      FXP policy rules (global defaults and per-section overrides).
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
/// FXP policy rules (global defaults and per-section overrides).
/// </summary>
public sealed class FxpPolicyConfig
{
    /// <summary>Enables policy evaluation. If false, FXP is effectively allowed (subject to global FxpConfig).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Allow FXP for non-admin users.</summary>
    public bool AllowUsers { get; init; } = true;

    /// <summary>Allow FXP for admin users.</summary>
    public bool AllowAdmins { get; init; } = true;

    /// <summary>
    /// If true, non-admin users must have an explicit "Allow FXP" flag
    /// resolved from user/group config.
    /// </summary>
    public bool RequireUserAllowFlag { get; init; } = true;

    /// <summary>Allow FXP Incoming (peer -> us, i.e. Download).</summary>
    public bool AllowIncoming { get; init; } = true;

    /// <summary>Allow FXP Outgoing (us -> peer, i.e. Upload).</summary>
    public bool AllowOutgoing { get; init; } = true;

    /// <summary>
    /// Deny FXP when the peer IP equals the control peer IP.
    /// Helps prevent trivial bounce/loop misuse.
    /// </summary>
    public bool DenySameHost { get; init; } = true;

    /// <summary>
    /// Sections that are explicitly denied for FXP. If empty, no sections
    /// are hard-denied here.
    /// </summary>
    public ISet<string> DenySections { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// If non-empty, only sections listed here may be used for FXP.
    /// </summary>
    public ISet<string> AllowSections { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Additional trusted hosts (host/IP/CIDR/wildcard). These are treated
    /// as allowed peers in addition to FxpConfig.AllowedPeers.
    /// </summary>
    public ISet<string> TrustedHosts { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit deny-list for peers (host/IP/CIDR/wildcard). If a peer
    /// matches any entry here, the FXP is denied.
    /// </summary>
    public ISet<string> DenyHosts { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// If true, the control connection must be TLS for FXP to be allowed
    /// at all.
    /// </summary>
    public bool RequireControlTls { get; init; } = false;

    /// <summary>
    /// If true, control and data TLS state must match (both secure or both
    /// plain) for FXP to be allowed.
    /// </summary>
    public bool RequireMatchingTls { get; init; } = false;

    /// <summary>
    /// Minimum TLS version required for Incoming FXP (Download direction).
    /// </summary>
    public TlsVersion MinTlsIncoming { get; init; } = TlsVersion.Tls12;

    /// <summary>
    /// Minimum TLS version required for Outgoing FXP (Upload direction).
    /// </summary>
    public TlsVersion MinTlsOutgoing { get; init; } = TlsVersion.Tls12;

    /// <summary>
    /// Optional ident string that must match the remote ident for FXP to
    /// be allowed (if RequireIdentMatch is true).
    /// </summary>
    public string? RequiredIdent { get; init; }

    /// <summary>
    /// If true and RequiredIdent is set, FXP is denied when the peer ident
    /// does not match.
    /// </summary>
    public bool RequireIdentMatch { get; init; } = false;

    /// <summary>
    /// Allow FXP where any leg is clear-text or control/data are mixed TLS/clear.
    /// </summary>
    public bool AllowPlainFxp { get; init; } = false;

    /// <summary>
    /// Allow FXP where both control and data are protected by TLS.
    /// </summary>
    public bool AllowSecureFxp { get; init; } = true;
    
    
}
