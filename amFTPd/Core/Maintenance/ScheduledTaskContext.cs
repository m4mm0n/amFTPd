/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03
 *  Last Modified:  2025-12-03
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using amFTPd.Config.Daemon;
using amFTPd.Logging;

namespace amFTPd.Core.Maintenance
{
    /// <summary>
    /// Represents the context for a scheduled task, providing access to runtime configuration and logging
    /// functionality.
    /// </summary>
    /// <remarks>This class encapsulates the runtime configuration and logging components required for
    /// executing a scheduled task. It ensures that both dependencies are provided and accessible during the task's
    /// execution.</remarks>
    public sealed class ScheduledTaskContext
    {
        public ScheduledTaskContext(AmFtpdRuntimeConfig runtime, IFtpLogger log)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public AmFtpdRuntimeConfig Runtime { get; }

        public IFtpLogger Log { get; }
    }
}
