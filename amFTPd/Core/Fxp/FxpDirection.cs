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