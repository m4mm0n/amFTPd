using amFTPd.Config.Daemon;
using amFTPd.Core.Maintenance;
using amFTPd.Logging;

namespace amFTPd.Core.Services;

/// <summary>
/// Periodic background maintenance tasks.
/// </summary>
/// <remarks>
/// Owns two loops:
/// <list type="bullet">
///   <item>A 5-minute housekeeping tick for lightweight work (PRE cleanup, etc.).</item>
///   <item>A <see cref="Scheduler"/> running calendar-based tasks (weekly/monthly stats snapshots).</item>
/// </list>
/// </remarks>
public sealed class HousekeepingService : IAsyncDisposable
{
    private readonly AmFtpdRuntimeConfig _runtime;
    private readonly IFtpLogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Scheduler _scheduler;

    public HousekeepingService(
        AmFtpdRuntimeConfig runtime,
        IFtpLogger log)
    {
        _runtime = runtime;
        _log = log;

        var taskContext = new ScheduledTaskContext(runtime, log);

        _scheduler = new Scheduler(taskContext,
        [
            new WeeklyStatsSnapshotTask(),
            new MonthlyStatsSnapshotTask()
        ]);

        _scheduler.Start();
        _loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), _cts.Token);

                var now = DateTimeOffset.UtcNow;

                var removedPre = _runtime.PreRegistry
                    .CleanupExpired(now, _runtime.PreTtl);
                if (removedPre > 0)
                    _log.Log(FtpLogLevel.Debug,
                        $"[PRE] Cleaned up {removedPre} expired PRE entries.");

                var removedReq = _runtime.RequestRegistry?
                    .CleanupExpired(now, TimeSpan.FromDays(30)) ?? 0;
                if (removedReq > 0)
                    _log.Log(FtpLogLevel.Debug,
                        $"[REQUESTS] Cleaned up {removedReq} expired/filled request(s).");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Warn,
                    "[PRE] Housekeeping error", ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _loop; } catch { }
        await _scheduler.DisposeAsync();
        _cts.Dispose();
    }
}
