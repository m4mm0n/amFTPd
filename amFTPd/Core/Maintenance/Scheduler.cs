/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           Scheduler.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 04:03:29
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x2B8D3290
 *  
 *  Description:
 *      Represents a scheduler that manages and executes a collection of scheduled tasks at specified intervals.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using amFTPd.Logging;

namespace amFTPd.Core.Maintenance;

/// <summary>
/// Represents a scheduler that manages and executes a collection of scheduled tasks at specified intervals.
/// </summary>
/// <remarks>The <see cref="Scheduler"/> class is designed to run tasks in the background on a recurring
/// schedule. Tasks are executed asynchronously, and the scheduler ensures that each task runs at its defined
/// interval. The scheduler starts its execution loop when the <see cref="Start"/> method is called and continues
/// until it is disposed. This class is thread-safe and can be used in multi-threaded environments.</remarks>
public sealed class Scheduler : IAsyncDisposable
{
    private readonly ScheduledTaskContext _context;
    private readonly IReadOnlyList<IScheduledTask> _tasks;
    private readonly Dictionary<IScheduledTask, DateTimeOffset> _nextRun =
        new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public Scheduler(
        ScheduledTaskContext context,
        IEnumerable<IScheduledTask> tasks)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tasks = tasks?.ToList() ?? throw new ArgumentNullException(nameof(tasks));

        var now = DateTimeOffset.UtcNow;
        foreach (var t in _tasks)
        {
            // Start all tasks "due" immediately.
            _nextRun[t] = now;
        }
    }

    /// <summary>
    /// Start the background scheduling loop.
    /// Safe to call only once.
    /// </summary>
    public void Start()
    {
        if (_loop is not null)
            return;

        _loop = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                // Find tasks that are due
                var due = _nextRun
                    .Where(kvp => kvp.Value <= now)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var task in due)
                {
                    _ = RunTaskOnceAsync(task, ct);
                    _nextRun[task] = now + task.Interval;
                }

                // Sleep until the next scheduled task or a small default
                var next = _nextRun.Values.DefaultIfEmpty(now + TimeSpan.FromSeconds(5))
                    .Min();

                var delay = next - DateTimeOffset.UtcNow;
                if (delay < TimeSpan.FromMilliseconds(100))
                    delay = TimeSpan.FromMilliseconds(100);

                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log and keep going
                try
                {
                    _context.Log.Log(FtpLogLevel.Error, $"Scheduler loop error: {ex}");
                }
                catch
                {
                    // last resort: swallow
                }
            }
        }
    }

    private async Task RunTaskOnceAsync(IScheduledTask task, CancellationToken ct)
    {
        try
        {
            _context.Log.Log(FtpLogLevel.Debug, $"Running scheduled task '{task.Name}'...");
            await task.RunAsync(_context, ct);
        }
        catch (TaskCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            try
            {
                _context.Log.Log(FtpLogLevel.Error, $"Scheduled task '{task.Name}' failed: {ex}");
            }
            catch
            {
                // swallow
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch
            {
                // ignore
            }
        }
        _cts.Dispose();
    }
}