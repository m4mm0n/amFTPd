using System.Collections.Concurrent;
using System.Diagnostics;

namespace amFTPd.Core.Stats.Rolling;

/// <summary>
/// Provides a thread-safe counter that tracks the sum of values added within a rolling time window and calculates the
/// rate per second over that window.
/// </summary>
/// <remarks>The rolling window is defined by the time span specified when constructing the instance. This class
/// is suitable for scenarios where you need to monitor the rate of events or values over a recent period, such as
/// requests per second or throughput metrics. All operations are safe for concurrent use by multiple threads.</remarks>
public sealed class RollingCounter
{
    private readonly TimeSpan _window;
    private readonly ConcurrentQueue<(long Ticks, long Value)> _samples = new();
    private long _total;
    private long _sum;

    public RollingCounter(TimeSpan window) => _window = window;

    public void Add(long value)
    {
        if (value <= 0)
            return;

        _sum += value;
    }

    public double RatePerSecond() =>
        _window.TotalSeconds <= 0
            ? 0.0
            : _sum / _window.TotalSeconds;

    public long Sum => _sum;

    private void Trim(long now)
    {
        var cutoff = now - (long)(_window.TotalSeconds * Stopwatch.Frequency);

        while (_samples.TryPeek(out var s) && s.Ticks < cutoff)
        {
            if (_samples.TryDequeue(out s))
                Interlocked.Add(ref _total, -s.Value);
        }
    }
}