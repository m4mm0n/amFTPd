/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IScheduledTask.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 04:03:29
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x276420A4
 *  
 *  Description:
 *      Represents a scheduled task that can be executed at regular intervals.
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
/// Represents a scheduled task that can be executed at regular intervals.
/// </summary>
/// <remarks>Implementations of this interface define the behavior of a task that is executed by a
/// scheduler.  The scheduler invokes the <see cref="RunAsync"/> method at the specified <see cref="Interval"/>. 
/// Tasks should handle exceptions internally if desired, or allow them to propagate for the scheduler  to log and
/// continue execution.</remarks>
public interface IScheduledTask
{
    /// <summary>Logical name for logging / status.</summary>
    string Name { get; }

    /// <summary>How often this task should run.</summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Execute the task body.
    /// Implementation should be resilient: catch & log internally if you want,
    /// or let exceptions bubble (scheduler will log and continue).
    /// </summary>
    Task RunAsync(ScheduledTaskContext context, CancellationToken cancellationToken);
}