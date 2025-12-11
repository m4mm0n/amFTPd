/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           HammerDecision.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-11 02:59:58
 *  Last Modified:  2025-12-11 03:00:25
 *  CRC32:          0x28F230F4
 *  
 *  Description:
 *      Result of a hammer / abuse check.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

namespace amFTPd.Security.HammerGuard
{
    /// <summary>
    /// Result of a hammer / abuse check.
    /// </summary>
    public sealed record HammerDecision(
        bool ShouldThrottle,
        bool ShouldBan,
        TimeSpan ThrottleDelay,
        TimeSpan? BanDuration,
        string? Reason)
    {
        public static HammerDecision None { get; } =
            new(false, false, TimeSpan.Zero, null, null);

        public bool IsNoop => !ShouldThrottle && !ShouldBan;
    }
}
