/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           QuickLogAdapter.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xA7FD133B
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Logging;

public sealed class QuickLogAdapter : IFtpLogger
{
    // private readonly IQuickLog _ql;
    // public QuickLogAdapter(IQuickLog ql) => _ql = ql;

    public void Log(FtpLogLevel level, string message, Exception? ex = null)
    {
        // Map to QuickLog levels here.
        // _ql.Log(...);
        Console.WriteLine($"[QL:{level}] {message}");
        if (ex != null) Console.WriteLine(ex);
    }
}