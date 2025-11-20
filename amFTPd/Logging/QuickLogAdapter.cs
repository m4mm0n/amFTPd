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