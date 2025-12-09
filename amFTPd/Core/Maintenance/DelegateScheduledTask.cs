/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           DelegateScheduledTask.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 04:03:29
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x5FE4572A
 *  
 *  Description:
 *      Represents a scheduled task that executes a delegate function at a specified interval.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core.Maintenance;

/// <summary>
/// Represents a scheduled task that executes a delegate function at a specified interval.
/// </summary>
/// <remarks>This class allows you to define a scheduled task by providing a delegate function to be
/// executed. The task is identified by a name and runs at a specified interval. The delegate function receives a
/// <see cref="ScheduledTaskContext"/> and a <see cref="CancellationToken"/> as parameters.</remarks>
public sealed class DelegateScheduledTask : IScheduledTask
{
    private readonly Func<ScheduledTaskContext, CancellationToken, Task> _callback;

    public DelegateScheduledTask(
        string name,
        TimeSpan interval,
        Func<ScheduledTaskContext, CancellationToken, Task> callback)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Interval = interval;
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public string Name { get; }

    public TimeSpan Interval { get; }

    public Task RunAsync(ScheduledTaskContext context, CancellationToken cancellationToken)
        => _callback(context, cancellationToken);
}