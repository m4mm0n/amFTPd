/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           PerfCounters.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 00:54:02
 *  Last Modified:  2025-12-14 20:11:47
 *  CRC32:          0x2C08F39E
 *  
 *  Description:
 *      Simple, lock-free-ish performance counters for server and transfer stats. Intended for stats/monitoring; no guarantee...
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
    /// Best-effort, non-authoritative runtime telemetry.
    /// </summary>
    /// <remarks>
    /// PerfCounters are intended strictly for observability, monitoring,
    /// diagnostics, and external reporting.
    ///
    /// Values are approximate, may be temporarily inconsistent, and MUST NOT
    /// be used for enforcement, security decisions, throttling, or policy logic.
    /// 
    /// All behavioral decisions must be made by authoritative components
    /// (e.g. FtpServer, HammerGuard, session state).
    /// </remarks>
    public static class PerfCounters
    {
        // --- Connection / command level ------------------------------------
        private static long _activeConnections;
        private static long _totalConnections;
        private static long _totalCommands;
        private static long _failedLogins;
        private static long _abortedTransfers;

        // --- Transfer level -------------------------------------------------
        private static long _bytesUploaded;
        private static long _bytesDownloaded;
        private static long _activeTransfers;
        private static long _totalTransfers;
        private static long _totalTransferMilliseconds;
        private static long _maxConcurrentTransfers;
        private static long _transferTimeTicks;

        // --------------------------------------------------------------------
        // Connection counters
        // --------------------------------------------------------------------

        /// <summary>Call when a new control connection is accepted.</summary>
        public static void ConnectionOpened()
        {
            Interlocked.Increment(ref _activeConnections);
            Interlocked.Increment(ref _totalConnections);
        }

        /// <summary>Call when a control connection is closed (normal or error).</summary>
        public static void ConnectionClosed()
        {
            Interlocked.Decrement(ref _activeConnections);
        }

        /// <summary>Call after a successfully parsed + dispatched command.</summary>
        public static void CommandExecuted()
            => Interlocked.Increment(ref _totalCommands);

        /// <summary>Call when a login attempt fails.</summary>
        public static void FailedLogin()
            => Interlocked.Increment(ref _failedLogins);

        /// <summary>Call when a transfer is aborted (ABOR, IO error, etc.).</summary>
        public static void TransferAborted()
            => Interlocked.Increment(ref _abortedTransfers);

        // --------------------------------------------------------------------
        // Transfer counters
        // --------------------------------------------------------------------

        /// <summary>Called when a data transfer (upload or download) starts.</summary>
        public static void ObserveTransferStarted()
        {
            var current = Interlocked.Increment(ref _activeTransfers);
            Interlocked.Increment(ref _totalTransfers);

            long prev;
            do
            {
                prev = Interlocked.Read(ref _maxConcurrentTransfers);
                if (current <= prev)
                    break;
            }
            while (Interlocked.CompareExchange(
                       ref _maxConcurrentTransfers,
                       current,
                       prev) != prev);
        }

        /// <summary>Called when a data transfer completes.</summary>
        public static void ObserveTransferCompleted(TimeSpan elapsed)
        {
            Interlocked.Decrement(ref _activeTransfers);
            Interlocked.Add(ref _transferTimeTicks, elapsed.Ticks);
        }

        /// <summary>Adds bytes for a completed transfer.</summary>
        public static void AddBytesTransferred(long bytes, bool isDownload)
        {
            if (bytes <= 0) return;

            if (isDownload)
                Interlocked.Add(ref _bytesDownloaded, bytes);
            else
                Interlocked.Add(ref _bytesUploaded, bytes);
        }

        /// <summary>
        /// Adds the specified number of bytes to the total uploaded bytes counter in a thread-safe manner.
        /// </summary>
        /// <param name="bytes">The number of bytes to add to the uploaded bytes total. Must be greater than zero; values less than or equal
        /// to zero are ignored.</param>
        public static void AddUploadedBytes(long bytes)
        {
            if (bytes > 0)
                Interlocked.Add(ref _bytesUploaded, bytes);
        }
        
        /// <summary>
        /// Adds the specified number of bytes to the total downloaded byte count in a thread-safe manner.
        /// </summary>
        /// <param name="bytes">The number of bytes to add to the total downloaded count. Must be greater than 0 to have an effect.</param>
        public static void AddDownloadedBytes(long bytes)
        {
            if (bytes > 0)
                Interlocked.Add(ref _bytesDownloaded, bytes);
        }

        /// <summary>
        /// Returns a point-in-time snapshot of telemetry counters.
        /// </summary>
        /// <remarks>
        /// Snapshot values are not guaranteed to be internally consistent
        /// and should be treated as approximate.
        /// </remarks>
        public static PerfSnapshot GetSnapshot()
        {
            var transfers = Interlocked.Read(ref _totalTransfers);

            return new PerfSnapshot
            {
                ActiveConnections = Interlocked.Read(ref _activeConnections),
                TotalConnections = Interlocked.Read(ref _totalConnections),

                ActiveTransfers = Interlocked.Read(ref _activeTransfers),
                TotalTransfers = transfers,

                BytesUploaded = Interlocked.Read(ref _bytesUploaded),
                BytesDownloaded = Interlocked.Read(ref _bytesDownloaded),

                FailedLogins = Interlocked.Read(ref _failedLogins),
                AbortedTransfers = Interlocked.Read(ref _abortedTransfers),

                TotalCommands = Interlocked.Read(ref _totalCommands),

                AverageTransferMilliseconds =
                    transfers > 0
                        ? TimeSpan.FromTicks(
                                Interlocked.Read(ref _transferTimeTicks) / transfers)
                            .TotalMilliseconds
                        : 0.0,

                MaxConcurrentTransfers =
                    Interlocked.Read(ref _maxConcurrentTransfers)
            };
        }
    }
}
