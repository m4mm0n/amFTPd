/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpRequest.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:55:02
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x2164BD75
 *  
 *  Description:
 *      Normalized FXP request data passed into the policy. This is intentionally decoupled from FtpUser/FtpSection.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core.Fxp
{
    /// <summary>
    /// Normalized FXP request data passed into the policy.
    /// This is intentionally decoupled from FtpUser/FtpSection.
    /// </summary>
    public sealed class FxpRequest
    {
        /// <summary>Connected username on this site.</summary>
        public string UserName { get; init; } = string.Empty;

        /// <summary>Primary group name (if any).</summary>
        public string? GroupName { get; init; }

        /// <summary>Logical section name (e.g. "MP3").</summary>
        public string? SectionName { get; init; }

        /// <summary>Virtual path on this site for the FXP target/source.</summary>
        public string? VirtualPath { get; init; }

        /// <summary>Remote host (IP or hostname) of the peer site.</summary>
        public string RemoteHost { get; init; } = string.Empty;

        /// <summary>Optional remote ident / user hint.</summary>
        public string? RemoteIdent { get; init; }

        /// <summary>Whether this user has AllowFxp set.</summary>
        public bool UserAllowFxp { get; init; }

        /// <summary>Whether this user is an administrator.</summary>
        public bool IsAdmin { get; init; }

        /// <summary>Direction of FXP from the perspective of this site.</summary>
        public FxpDirection Direction { get; init; } = FxpDirection.Incoming;
    }
}
