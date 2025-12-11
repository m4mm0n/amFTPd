/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           HammerState.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-11 03:00:22
 *  Last Modified:  2025-12-11 03:00:22
 *  CRC32:          0xF4AEEB76
 *  
 *  Description:
 *      Per-IP activity snapshot for hammer/flood detection.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

namespace amFTPd.Security.HammerGuard;

/// <summary>
/// Per-IP activity snapshot for hammer/flood detection.
/// </summary>
internal sealed class HammerState
{
    public DateTime LastTouchedUtc;

    // Failed login tracking
    public int FailedLoginCount;
    public DateTime FirstFailureUtc;

    // Command-rate window (per-IP, not per-session)
    public int WindowCommandCount;
    public DateTime WindowStartUtc;
}