/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           CancellableTask.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 01:13:33
 *  Last Modified:  2025-12-14 01:13:33
 *  CRC32:          0xAC4ECCF4
 *  
 *  Description:
 *      Represents a long-running operation that can be cancelled and awaited.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Utils;

/// <summary>
/// Represents a long-running operation that can be cancelled and awaited.
/// </summary>
public sealed class CancellableTask : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;

    public Task Task { get; }

    private CancellableTask(Task task, CancellationTokenSource cts)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        _cts = cts ?? throw new ArgumentNullException(nameof(cts));
    }

    /// <summary>
    /// Starts a cancellable background task.
    /// </summary>
    public static CancellableTask Run(
        Func<CancellationToken, Task> worker,
        CancellationToken externalCancellation = default)
    {
        if (worker is null) throw new ArgumentNullException(nameof(worker));

        var linkedCts = externalCancellation.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalCancellation)
            : new CancellationTokenSource();

        var task = Task.Run(() => worker(linkedCts.Token), linkedCts.Token);
        return new CancellableTask(task, linkedCts);
    }

    public void Cancel() => _cts.Cancel();

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();

        try
        {
            await Task.ConfigureAwait(false);
        }
        catch
        {
            // We don't care about the outcome here; caller deliberately disposed.
        }
    }
}