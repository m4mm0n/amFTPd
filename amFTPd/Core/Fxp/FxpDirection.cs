/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpDirection.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:57:30
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xF36C582E
 *  
 *  Description:
 *      Direction of FXP transfer from the perspective of *this* site.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core.Fxp;

/// <summary>
/// Direction of FXP transfer from the perspective of *this* site.
/// </summary>
public enum FxpDirection
{
    Incoming,  // remote -> here
    Outgoing,  // here -> remote
    Both       // used only in config shortcuts
}