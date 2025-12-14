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
    /// Simple, lock-free-ish performance counters for server and transfer stats.
    /// Intended for stats/monitoring; no guarantees on perfect precision.
    /// </summary>
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
            while (true)
            {
                var current = Volatile.Read(ref _activeConnections);
                if (current <= 0)
                    return;

                if (Interlocked.CompareExchange(ref _activeConnections, current - 1, current) == current)
                    return;
            }
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
        public static void OnTransferStarted()
        {
            var current = Interlocked.Increment(ref _activeTransfers);

            // Track peak concurrent transfers
            while (true)
            {
                var snapshot = Volatile.Read(ref _maxConcurrentTransfers);
                if (current <= snapshot)
                    break;

                if (Interlocked.CompareExchange(ref _maxConcurrentTransfers, current, snapshot) == snapshot)
                    break;
            }
        }

        /// <summary>Called when a data transfer completes.</summary>
        public static void OnTransferCompleted(TimeSpan duration)
        {
            Interlocked.Decrement(ref _activeTransfers);
            Interlocked.Increment(ref _totalTransfers);
            Interlocked.Add(ref _totalTransferMilliseconds, (long)duration.TotalMilliseconds);
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
        /// Represents an immutable snapshot of statistics at a specific point in time.
        /// </summary>
        public readonly struct Snapshot
        {
            // Connections / commands
            public long ActiveConnections { get; init; }
            public long TotalConnections { get; init; }
            public long TotalCommands { get; init; }
            public long FailedLogins { get; init; }
            public long AbortedTransfers { get; init; }

            // Transfers
            public long BytesUploaded { get; init; }
            public long BytesDownloaded { get; init; }
            public long ActiveTransfers { get; init; }
            public long TotalTransfers { get; init; }
            public double AverageTransferMilliseconds { get; init; }
            public long MaxConcurrentTransfers { get; init; }
        }

        /// <summary>
        /// Returns a snapshot of the current statistics.
        /// </summary>
        public static Snapshot GetSnapshot()
        {
            var transfers = Volatile.Read(ref _totalTransfers);
            var totalMs = Volatile.Read(ref _totalTransferMilliseconds);

            return new Snapshot
            {
                // connections / commands
                ActiveConnections = Volatile.Read(ref _activeConnections),
                TotalConnections = Volatile.Read(ref _totalConnections),
                TotalCommands = Volatile.Read(ref _totalCommands),
                FailedLogins = Volatile.Read(ref _failedLogins),
                AbortedTransfers = Volatile.Read(ref _abortedTransfers),

                // transfers
                BytesUploaded = Volatile.Read(ref _bytesUploaded),
                BytesDownloaded = Volatile.Read(ref _bytesDownloaded),
                ActiveTransfers = Volatile.Read(ref _activeTransfers),
                TotalTransfers = transfers,
                AverageTransferMilliseconds = transfers == 0 ? 0.0 : (double)totalMs / transfers,
                MaxConcurrentTransfers = Volatile.Read(ref _maxConcurrentTransfers)
            };
        }
    }
}
