/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpDataPolicyException.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-13 21:06:27
 *  Last Modified:  2025-12-13 21:07:25
 *  CRC32:          0xC9C16195
 *  
 *  Description:
 *      Used to abort a data transfer due to a local security/policy decision. The FTP reply line is carried with the excepti...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Core.Fxp
{
    /// <summary>
    /// Used to abort a data transfer due to a local security/policy decision.
    /// The FTP reply line is carried with the exception and must include a
    /// status code (e.g. 550) and CRLF.
    /// </summary>
    public sealed class FtpDataPolicyException : Exception
    {
        /// <summary>
        /// Gets the reply line returned by the server in response to a command.
        /// </summary>
        public string ReplyLine { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpDataPolicyException"/> class using the specified FTP server
        /// reply line.
        /// </summary>
        /// <remarks>The <paramref name="replyLine"/> is normalized before being used as the exception
        /// message and is available via the <see cref="ReplyLine"/> property.</remarks>
        /// <param name="replyLine">The reply line received from the FTP server that describes the data policy violation.  Cannot be <see
        /// langword="null"/>.</param>
        public FtpDataPolicyException(string replyLine)
            : base(Normalize(replyLine)) =>
            ReplyLine = Normalize(replyLine);

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "550 Denied by policy.\r\n";

            s = s.Replace("\r", string.Empty);
            if (!s.EndsWith("\n", StringComparison.Ordinal))
                s += "\r\n";
            if (!s.StartsWith("5", StringComparison.Ordinal))
                s = "550 " + s.TrimStart();
            return s;
        }
    }

}
