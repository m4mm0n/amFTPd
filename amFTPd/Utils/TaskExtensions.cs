/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           TaskExtensions.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 01:13:33
 *  Last Modified:  2025-12-14 01:13:33
 *  CRC32:          0xF19748BA
 *  
 *  Description:
 *      Task extension methods.
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
/// Task extension methods.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Fire-and-forget helper that observes exceptions so they don't get swallowed
    /// by the finalizer thread. No allocations when the task is already completed.
    /// </summary>
    public static void Forget(this Task task)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));

        if (task.IsCompleted)
        {
            // Observe any exception
            _ = task.Exception;
            return;
        }

        task.ContinueWith(
            t => { _ = t.Exception; },
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
    }
}