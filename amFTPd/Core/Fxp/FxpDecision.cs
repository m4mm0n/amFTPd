/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpDecision.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:57:30
 *  Last Modified:  2025-12-13 21:12:25
 *  CRC32:          0x01BCA8F0
 *  
 *  Description:
 *      Result of FXP policy evaluation.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


namespace amFTPd.Core.Fxp;

/// <summary>
/// Result of FXP policy evaluation.
/// </summary>
public sealed record FxpDecision(bool Allowed, string? DenyReason)
{
    public static FxpDecision Allow() => new(true, null);
    public static FxpDecision Deny(string reason) => new(false, reason);
}
