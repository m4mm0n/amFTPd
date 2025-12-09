/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpDecision.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:57:30
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xAF46B054
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
 * ==================================================================================================== */





namespace amFTPd.Core.Fxp;

/// <summary>
/// Result of FXP policy evaluation.
/// </summary>
public sealed class FxpDecision
{
    /// <summary>True if this FXP request is allowed.</summary>
    public bool Allowed { get; init; }

    /// <summary>Human-readable deny reason (if not allowed).</summary>
    public string? DenyReason { get; init; }

    public static readonly FxpDecision Allow = new() { Allowed = true };

    public static FxpDecision Deny(string reason) => new()
    {
        Allowed = false,
        DenyReason = reason
    };
}