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

using amFTPd.Config.Fxp;

namespace amFTPd.Core.Fxp;

/// <summary>
/// Provides functionality to evaluate FXP (File eXchange Protocol) requests based on a configurable set of
/// policies.
/// </summary>
/// <remarks>The <see cref="FxpPolicyEngine"/> evaluates FXP requests by applying rules defined in the
/// provided <see cref="FxpPolicyConfig"/>. These rules include checks for request direction, user permissions,
/// section-based restrictions, host-based rules, and other configurable criteria.</remarks>
public sealed class FxpPolicyEngine
{
    private readonly FxpPolicyConfig _cfg;

    public FxpPolicyEngine(FxpPolicyConfig cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
    }

    public FxpDecision Evaluate(FxpRequest req)
    {
        if (!_cfg.Enabled)
            return FxpDecision.Deny("FXP is disabled.");

        // Direction checks
        if (req.Direction == FxpDirection.Incoming && !_cfg.AllowIncoming)
            return FxpDecision.Deny("Incoming FXP is disabled.");

        if (req.Direction == FxpDirection.Outgoing && !_cfg.AllowOutgoing)
            return FxpDecision.Deny("Outgoing FXP is disabled.");

        // Admin bypass logic (early exit)
        if (req.IsAdmin && _cfg.AdminsBypass)
        {
            // still might want to block hard-deny sections or hosts, but let's keep it simple for v1
            return FxpDecision.Allow;
        }

        // User-level flag
        if (_cfg.RequireUserAllowFlag && !req.UserAllowFxp)
            return FxpDecision.Deny("User FXP flag is not set.");

        // Section allow/deny
        if (!string.IsNullOrEmpty(req.SectionName))
        {
            var sec = req.SectionName;

            if (_cfg.DenySections.Contains(sec))
                return FxpDecision.Deny($"FXP is not allowed in section '{sec}'.");

            if (_cfg.AllowSections.Count > 0 && !_cfg.AllowSections.Contains(sec))
                return FxpDecision.Deny($"FXP is not allowed in section '{sec}'.");
        }

        // Host-based rules
        if (_cfg.DenySameHost && IsSameHost(req.RemoteHost, req.VirtualPath))
        {
            // VirtualPath might not carry host info; this is a placeholder check.
            // In practice you'd pass local listening address / peer IP separately.
            return FxpDecision.Deny("FXP to the same host is not allowed.");
        }

        if (_cfg.TrustedHosts.Count > 0 && !IsTrustedHost(req.RemoteHost))
            return FxpDecision.Deny($"FXP to remote host '{req.RemoteHost}' is not allowed.");

        // Ident / user matching rule
        if (_cfg.RequireMatchingIdentForUser &&
            !string.IsNullOrEmpty(req.RemoteIdent) &&
            !string.IsNullOrEmpty(req.UserName))
        {
            if (!req.RemoteIdent.Equals(req.UserName, StringComparison.OrdinalIgnoreCase))
                return FxpDecision.Deny("FXP requires matching remote ident and local username.");
        }

        return FxpDecision.Allow;
    }

    private bool IsTrustedHost(string remoteHost)
    {
        if (string.IsNullOrWhiteSpace(remoteHost))
            return false;

        if (_cfg.TrustedHosts.Contains(remoteHost))
            return true;

        // simple wildcard-ish: "irc.*.net", "*.scene.org", etc. can be added later if you want
        // For now, exact match only.
        return false;
    }

    private static bool IsSameHost(string remoteHost, string? localContext)
    {
        // v1: we don't have local host info here, so treat this as "never same host".
        // Later you can extend FxpRequest with LocalHost/LocalIp and do a real comparison.
        _ = remoteHost;
        _ = localContext;
        return false;
    }
}