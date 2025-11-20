/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
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

namespace amFTPd.Core
{
    /// <summary>
    /// Provides predefined FTP response messages and a method for generating custom banner responses.
    /// </summary>
    /// <remarks>This class contains a collection of static constants representing common FTP server response
    /// messages, as defined by the FTP protocol. These responses are formatted according to the standard FTP reply
    /// codes and can be used to communicate status and results to FTP clients. <para> The <see cref="Banner(string)"/>
    /// method allows for the creation of a custom FTP banner message, which is typically sent to clients upon
    /// establishing a connection. </para></remarks>
    internal static class FtpResponses
    {
        public static string Banner(string msg) => $"220 {msg}\r\n";
        public const string Bye = "221 Goodbye.\r\n";
        public const string Ok = "200 OK\r\n";
        public const string NotLoggedIn = "530 Not logged in.\r\n";
        public const string NeedPassword = "331 Password required.\r\n";
        public const string AuthOk = "230 User logged in, proceed.\r\n";
        public const string UnknownCmd = "502 Command not implemented.\r\n";
        public const string CmdOkay = "200 Command okay.\r\n";
        public const string TypeSetBinary = "200 Type set to I.\r\n";
        public const string TypeSetAscii = "200 Type set to A.\r\n";
        public const string ActionOk = "250 Requested file action okay, completed.\r\n";
        public const string FileOk = "150 File status okay; about to open data connection.\r\n";
        public const string ClosingData = "226 Closing data connection.\r\n";
        public const string PathCreated = "257 Path created.\r\n";
        public const string BadSeq = "503 Bad sequence of commands.\r\n";
        public const string SyntaxErr = "501 Syntax error in parameters.\r\n";
        public const string ProtOk = "200 Protection level set.\r\n";
        public const string PbszOk = "200 PBSZ=0 OK.\r\n";
        public const string TlsReady = "234 AUTH TLS successful.\r\n";
    }
}
