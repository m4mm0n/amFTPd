/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpRequest.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:55:02
 *  Last Modified:  2025-12-13 22:43:53
 *  CRC32:          0x3F5FF89F
 *  
 *  Description:
 *      Context for a single FXP attempt, as constructed by FtpCommandRouter. This matches what the router is currently setting.
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
using System.Net;
using System.Security.Authentication;

namespace amFTPd.Core.Fxp
{
    /// <summary>
    /// Context for a single FXP attempt, as constructed by FtpCommandRouter.
    /// This matches what the router is currently setting.
    /// </summary>
    public sealed record FxpRequest
    {
        public required string UserName { get; init; }
        public string? GroupName { get; init; }

        public bool IsAdmin { get; init; }
        public bool UserAllowFxp { get; init; }

        public string? SectionName { get; init; }
        public required string VirtualPath { get; init; }

        public required FxpDirection Direction { get; init; }

        public required string RemoteHost { get; init; }
        public IPAddress? RemoteIp { get; init; }
        public IPAddress? ControlPeerIp { get; init; }
        public string? RemoteIdent { get; init; }

        /// <summary>Whether the control connection is TLS.</summary>
        public bool ControlTlsActive { get; init; }

        /// <summary>Control connection TLS protocol (nullable).</summary>
        public SslProtocols? ControlProtocol { get; init; }

        public string? ControlCipherSuite { get; init; }

        /// <summary>Whether data is expected to be protected (PROT P).</summary>
        public bool DataChannelProtected { get; init; }

        /// <summary>Whether the data channel actually negotiated TLS (if known).</summary>
        public bool DataTlsActive { get; init; }

        /// <summary>Data TLS protocol (nullable).</summary>
        public SslProtocols? DataProtocol { get; init; }

        public string? DataCipherSuite { get; init; }
    }
}
