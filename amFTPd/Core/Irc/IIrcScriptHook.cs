/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IIrcScriptHook.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 04:32:48
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x3BA6BC45
 *  
 *  Description:
 *      Hook interface for custom IRC logic (AMScript or otherwise). You can implement this using your AMScript engine and wi...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core.Irc;

/// <summary>
/// Hook interface for custom IRC logic (AMScript or otherwise).
/// You can implement this using your AMScript engine and wire it into IrcAnnouncer.
/// </summary>
public interface IIrcScriptHook
{
    /// <summary>
    /// Called after the socket is connected and TLS (if any) is established,
    /// but before the default PASS/NICK/USER/JOIN are sent.
    /// Return true if you fully handled registration yourself (script sends all commands),
    /// or false to let IrcAnnouncer send its default registration.
    /// </summary>
    Task<bool> OnRegisterAsync(IrcScriptContext ctx);

    /// <summary>
    /// Called after registration + JOIN have completed (or script-based register),
    /// to allow script to perform any extra setup.
    /// </summary>
    Task OnConnectedAsync(IrcScriptContext ctx);

    /// <summary>
    /// Called for each incoming IRC line (after minimal internal processing like PING/PONG).
    /// </summary>
    Task OnIncomingLineAsync(IrcScriptContext ctx, string line);
}