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

using amFTPd.Config.Irc;
using amFTPd.Logging;

namespace amFTPd.Core.Irc;

/// <summary>
/// Lightweight context passed to the IRC script hook for sending raw lines and logging.
/// You can wrap AMScript inside an implementation of IIrcScriptHook.
/// </summary>
public sealed class IrcScriptContext
{
    private readonly Func<string, Task> _sendRaw;

    public IrcScriptContext(IrcConfig config, IFtpLogger log, Func<string, Task> sendRaw)
    {
        Config = config;
        Log = log;
        _sendRaw = sendRaw;
    }

    public IrcConfig Config { get; }
    public IFtpLogger Log { get; }

    /// <summary>Send a raw IRC line, without CRLF.</summary>
    public Task SendRawAsync(string rawLine) => _sendRaw(rawLine);
}