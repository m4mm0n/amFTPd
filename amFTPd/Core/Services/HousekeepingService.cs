using amFTPd.Config.Daemon;
using amFTPd.Logging;

namespace amFTPd.Core.Services;

/// <summary>
/// Periodic background maintenance tasks.
/// </summary>
public sealed class HousekeepingService : IAsyncDisposable
{
    private readonly AmFtpdRuntimeConfig _runtime;
    private readonly IFtpLogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public HousekeepingService(
        AmFtpdRuntimeConfig runtime,
        IFtpLogger log)
    {
        _runtime = runtime;
        _log = log;

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
                var removed = _runtime.PreRegistry
                    .CleanupExpired(now, _runtime.PreTtl);

                if (removed > 0)
                {
                    _log.Log(
                        FtpLogLevel.Debug,
                        $"[PRE] Cleaned up {removed} expired PRE entries.");
                }
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
        _cts.Dispose();
    }
}
