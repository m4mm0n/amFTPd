/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-13 15:47:24
 *  Last Modified:  2025-12-13 21:09:10
 *  CRC32:          0xDCFEE77C
 *  
 *  Description:
 *      Global FXP behavior knobs. Section-specific overrides live in <see cref="FxpPolicyConfig"/>.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Config.Fxp
{
    /// <summary>
    /// Global FXP behavior knobs. Section-specific overrides live in
    /// <see cref="FxpPolicyConfig"/>.
    /// </summary>
    public sealed class FxpConfig
    {
        /// <summary>Master FXP switch. If false, FXP is disabled globally.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>Allow FXP when the peer side is plaintext (no TLS).</summary>
        public bool AllowPlainFxp { get; init; } = false;

        /// <summary>Allow FXP when the peer side uses TLS on both legs.</summary>
        public bool AllowSecureFxp { get; init; } = true;

        /// <summary>
        /// Peer allow-list. If empty => allow all peers (subject to other rules).
        /// Accepted formats:
        /// - IP:           203.0.113.5
        /// - CIDR:         203.0.113.0/24
        /// - Hostname:     fxp.example.org
        /// - Wildcard:     *.example.org
        /// </summary>
        public ISet<string> AllowedPeers { get; init; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// If true for Upload, local control TLS and remote data TLS must both
        /// either be secure or both plain.
        /// </summary>
        public bool RequireMatchingTlsUpload { get; init; } = true;

        /// <summary>
        /// If true for Download, local control TLS and remote data TLS must both
        /// either be secure or both plain.
        /// </summary>
        public bool RequireMatchingTlsDownload { get; init; } = true;

        /// <summary>
        /// Minimum TLS version required for Upload direction when FXP is secure.
        /// </summary>
        public TlsVersion MinTlsUpload { get; init; } = TlsVersion.Tls12;

        /// <summary>
        /// Minimum TLS version required for Download direction when FXP is secure.
        /// </summary>
        public TlsVersion MinTlsDownload { get; init; } = TlsVersion.Tls12;
    }
}
