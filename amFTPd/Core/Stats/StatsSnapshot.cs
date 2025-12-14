/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           StatsSnapshot.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 20:12:08
 *  Last Modified:  2025-12-14 20:12:31
 *  CRC32:          0xE7E4B719
 *  
 *  Description:
 *      Represents a point-in-time snapshot of overall server stats.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Core.Stats
{
    /// <summary>
    /// Represents a point-in-time snapshot of overall server stats.
    /// </summary>
    public sealed record StatsSnapshot(
        DateTimeOffset CapturedAtUtc,
        long ActiveConnections,
        long TotalConnections,
        long TotalCommands,
        long FailedLogins,
        long AbortedTransfers,
        long BytesUploaded,
        long BytesDownloaded,
        long ActiveTransfers,
        long TotalTransfers,
        double AverageTransferMilliseconds,
        long MaxConcurrentTransfers)
    {
        /// <summary>
        /// Captures a snapshot using the current <see cref="PerfCounters"/> state.
        /// </summary>
        public static StatsSnapshot Capture()
        {
            var perf = PerfCounters.GetSnapshot();

            return new StatsSnapshot(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                ActiveConnections: perf.ActiveConnections,
                TotalConnections: perf.TotalConnections,
                TotalCommands: perf.TotalCommands,
                FailedLogins: perf.FailedLogins,
                AbortedTransfers: perf.AbortedTransfers,
                BytesUploaded: perf.BytesUploaded,
                BytesDownloaded: perf.BytesDownloaded,
                ActiveTransfers: perf.ActiveTransfers,
                TotalTransfers: perf.TotalTransfers,
                AverageTransferMilliseconds: perf.AverageTransferMilliseconds,
                MaxConcurrentTransfers: perf.MaxConcurrentTransfers
            );
        }
    }
}
