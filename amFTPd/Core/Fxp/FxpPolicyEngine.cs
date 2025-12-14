/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpPolicyEngine.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:57:30
 *  Last Modified:  2025-12-14 00:26:10
 *  CRC32:          0x30FAA0E3
 *  
 *  Description:
 *      Central FXP policy evaluation. Uses your FxpTlsState (TlsVersion + cipher string), and does NOT depend on TlsConfig m...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Config.Fxp;
using amFTPd.Security;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace amFTPd.Core.Fxp;

/// <summary>
/// Central FXP policy evaluation. Uses your FxpTlsState (TlsVersion + cipher string),
/// and does NOT depend on TlsConfig methods at all.
/// </summary>
public sealed class FxpPolicyEngine
{
    private readonly FxpConfig _cfg;
    private readonly FxpPolicyConfig _policy;

    // Keep TlsConfig in the ctor so existing call sites still compile,
    // but we do not use it here anymore.
    public FxpPolicyEngine(FxpConfig cfg, FxpPolicyConfig policy, TlsConfig _)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public FxpPolicyEngine(FxpPolicyConfig policy, TlsConfig tls)
        : this(new FxpConfig(), policy, tls)
    {
        _ = tls; // suppress unused warning
    }

    public FxpDecision Evaluate(FxpRequest req)
    {
        if (!_cfg.Enabled)
            return FxpDecision.Deny("FXP is disabled globally.");

        if (!_policy.Enabled)
            return FxpDecision.Allow();

        // user / admin gating
        if (req.IsAdmin)
        {
            if (!_policy.AllowAdmins)
                return FxpDecision.Deny("FXP denied for admins by policy.");
        }
        else
        {
            if (!_policy.AllowUsers)
                return FxpDecision.Deny("FXP denied for users by policy.");

            if (_policy.RequireUserAllowFlag && !req.UserAllowFxp)
                return FxpDecision.Deny("User is not permitted to use FXP.");
        }

        // section rules
        if (!string.IsNullOrWhiteSpace(req.SectionName))
        {
            var section = req.SectionName!;

            if (_policy.DenySections.Contains(section))
                return FxpDecision.Deny($"FXP denied in section {section}.");

            if (_policy.AllowSections.Count > 0 &&
                !_policy.AllowSections.Contains(section))
            {
                return FxpDecision.Deny($"FXP not allowed in section {section}.");
            }
        }

        // same-host protection
        if (_policy.DenySameHost &&
            req.ControlPeerIp is not null &&
            req.RemoteIp is not null &&
            req.ControlPeerIp.Equals(req.RemoteIp))
        {
            return FxpDecision.Deny("FXP denied: same host as control connection.");
        }

        // peer allow/deny (host/IP/CIDR/wildcard)
        if (!IsPeerAllowed(req))
            return FxpDecision.Deny("FXP peer is not allowed by policy.");

        // direction toggles
        if (req.Direction == FxpDirection.Incoming && !_policy.AllowIncoming)
            return FxpDecision.Deny("Incoming FXP is disabled by policy.");

        if (req.Direction == FxpDirection.Outgoing && !_policy.AllowOutgoing)
            return FxpDecision.Deny("Outgoing FXP is disabled by policy.");

        // TLS semantics using flattened fields from FxpRequest
        var controlTls = req.ControlTlsActive;
        var dataProtected = req.DataChannelProtected;      // PROT P/C
        var dataTlsActive = req.DataTlsActive;


        // "secure FXP" = both control and data TLS
        var isSecure = controlTls && (dataTlsActive || dataProtected);

        if (isSecure)
        {
            if (!_cfg.AllowSecureFxp || !_policy.AllowSecureFxp)
                return FxpDecision.Deny("Secure FXP is disabled by configuration or policy.");
        }
        else
        {
            if (!_cfg.AllowPlainFxp || !_policy.AllowPlainFxp)
                return FxpDecision.Deny("Plain FXP is disabled by configuration or policy.");
        }

        // require control TLS?
        if (_policy.RequireControlTls && !controlTls)
            return FxpDecision.Deny("Control TLS is required for FXP.");

        // matching TLS requirement (policy + per-direction from cfg)
        var requireMatch = _policy.RequireMatchingTls;
        if (req.Direction == FxpDirection.Incoming && _cfg.RequireMatchingTlsDownload)
            requireMatch = true;
        if (req.Direction == FxpDirection.Outgoing && _cfg.RequireMatchingTlsUpload)
            requireMatch = true;

        if (requireMatch && controlTls != dataTlsActive)
            return FxpDecision.Deny("FXP denied: control/data TLS mismatch.");

        // Minimum TLS version check (if secure)
        if (isSecure)
        {
            var min = req.Direction == FxpDirection.Incoming
                // Incoming FXP == "Download" direction
                ? MaxTls(_policy.MinTlsIncoming, _cfg.MinTlsDownload)
                // Outgoing FXP == "Upload" direction
                : MaxTls(_policy.MinTlsOutgoing, _cfg.MinTlsUpload);

            if (!IsTlsStrongEnough(req, min))
                return FxpDecision.Deny($"FXP denied: TLS version below minimum ({min}).");
        }


        // ident policy
        if (_policy.RequireIdentMatch &&
            !string.IsNullOrWhiteSpace(_policy.RequiredIdent))
        {
            if (!string.Equals(_policy.RequiredIdent, req.RemoteIdent, StringComparison.Ordinal))
                return FxpDecision.Deny("FXP denied: ident mismatch.");
        }

        return FxpDecision.Allow();
    }

    private static TlsVersion MaxTls(TlsVersion a, TlsVersion b)
        => (TlsVersion)Math.Max((int)a, (int)b);

    /// <summary>
    /// Only uses numeric TLS version from your FxpTlsState. CipherSuite string is ignored.
    /// </summary>
    private static bool IsTlsStrongEnough(FxpRequest req, TlsVersion min)
    {
        if (!req.ControlTlsActive)
            return false;

        var ctlVer = MapToTlsVersion(req.ControlProtocol);
        if (ctlVer == TlsVersion.Any || (int)ctlVer < (int)min)
            return false;

        if (!req.DataTlsActive)
            return true;

        var dataVer = MapToTlsVersion(req.DataProtocol);
        if (dataVer == TlsVersion.Any || (int)dataVer < (int)min)
            return false;

        return true;
    }

    private static TlsVersion MapToTlsVersion(SslProtocols? proto)
    {
        if (proto is null)
            return TlsVersion.Any;

        var p = proto.Value;

        // SslProtocols is [Flags]; pick the highest version flag set.
        if ((p & SslProtocols.Tls13) == SslProtocols.Tls13)
            return TlsVersion.Tls13;
        if ((p & SslProtocols.Tls12) == SslProtocols.Tls12)
            return TlsVersion.Tls12;
        if ((p & SslProtocols.Tls11) == SslProtocols.Tls11)
            return TlsVersion.Tls11;
        if ((p & SslProtocols.Tls) == SslProtocols.Tls)
            return TlsVersion.Tls10;

        return TlsVersion.Any;
    }

    private bool IsPeerAllowed(FxpRequest req)
    {
        // hard deny-list first
        if (MatchesAny(_policy.DenyHosts, req))
            return false;

        var patterns = new List<string>();

        // policy "trusted" peers
        patterns.AddRange(_policy.TrustedHosts);

        // global config peers
        patterns.AddRange(_cfg.AllowedPeers);

        // no allow-list configured -> allow all peers (subject to other rules)
        if (patterns.Count == 0)
            return true;

        return MatchesAny(patterns, req);
    }

    private static bool MatchesAny(IEnumerable<string> patterns, FxpRequest req)
    {
        foreach (var p in patterns)
        {
            var pattern = p.Trim();
            if (pattern.Length == 0)
                continue;

            if (IsPeerMatch(pattern, req))
                return true;
        }

        return false;
    }

    private static bool IsPeerMatch(string pattern, FxpRequest req)
    {
        // CIDR
        if (TryParseCidr(pattern, out var net))
            return req.RemoteIp is not null && net.Contains(req.RemoteIp);

        // IP literal
        if (IPAddress.TryParse(pattern, out var ip))
            return req.RemoteIp is not null && req.RemoteIp.Equals(ip);

        // wildcard hostname
        if (pattern.Contains('*', StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(req.RemoteHost))
                return false;

            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(req.RemoteHost, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // plain hostname
        return string.Equals(req.RemoteHost, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseCidr(string s, out CidrNetwork net)
    {
        net = default;
        var slash = s.IndexOf('/');
        if (slash <= 0 || slash >= s.Length - 1)
            return false;

        if (!IPAddress.TryParse(s[..slash], out var ip))
            return false;

        if (!int.TryParse(s[(slash + 1)..], out var prefixBits))
            return false;

        net = new CidrNetwork(ip, prefixBits);
        return true;
    }

    private readonly struct CidrNetwork
    {
        private readonly IPAddress _network;
        private readonly byte[] _bytes;
        private readonly int _prefixBits;
        private readonly AddressFamily _family;

        public CidrNetwork(IPAddress network, int prefixBits)
        {
            _network = network;
            _bytes = network.GetAddressBytes();
            _family = network.AddressFamily;
            _prefixBits = prefixBits;

            var max = _family == AddressFamily.InterNetwork ? 32 : 128;
            if (prefixBits < 0 || prefixBits > max)
                throw new ArgumentOutOfRangeException(nameof(prefixBits), "Invalid CIDR prefix length.");
        }

        public bool Contains(IPAddress ip)
        {
            if (ip.AddressFamily != _family)
                return false;

            var other = ip.GetAddressBytes();
            var fullBytes = _prefixBits / 8;
            var remBits = _prefixBits % 8;

            for (var i = 0; i < fullBytes; i++)
            {
                if (other[i] != _bytes[i])
                    return false;
            }

            if (remBits == 0)
                return true;

            var mask = (byte)(0xFF << (8 - remBits));
            return (other[fullBytes] & mask) == (_bytes[fullBytes] & mask);
        }
    }
}
