using System.Diagnostics;

namespace amFTPd.Core.Stats
{
    /// <summary>
    /// Provides periodic collection of performance statistics snapshots at a specified interval.
    /// </summary>
    /// <remarks>The StatsCollector class is designed to minimize the overhead of frequent statistics sampling
    /// by caching the most recent snapshot and only updating it after the configured interval has elapsed. This class
    /// is not thread-safe; if used from multiple threads, external synchronization is required.</remarks>
    public sealed class StatsCollector
    {
        private readonly TimeSpan _interval;
        private PerfSnapshot? _last;
        private StatsSnapshot _latest = StatsSnapshot.Empty;
        private long _lastTicks;

        public StatsCollector(TimeSpan interval)
        {
            _interval = interval;
            _lastTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Returns the authoritative runtime stats snapshot.
        /// All external consumers MUST use this method.
        /// </summary>
        public StatsSnapshot GetSnapshot()
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = TimeSpan.FromSeconds(
                (now - _lastTicks) / (double)Stopwatch.Frequency);

            if (elapsed < _interval)
                return _latest;

            _lastTicks = now;

            var current = PerfCounters.GetSnapshot();
            _latest = StatsSnapshot.FromCounters(current, _last, elapsed);
            _last = current;

            return _latest;
        }
    }
}
