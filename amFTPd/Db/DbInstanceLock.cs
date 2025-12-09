/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-01
 *  Last Modified:  2025-12-01
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

using System.Text;

namespace amFTPd.Db
{
    /// <summary>
    /// Simple process-level lock for a database directory. Ensures that only one
    /// amFTPd process is using the binary DB in a given base directory at a time.
    /// </summary>
    internal sealed class DbInstanceLock : IDisposable
    {
        private readonly FileStream _lockStream;

        public string LockFilePath { get; }

        private DbInstanceLock(string lockFilePath, FileStream lockStream)
        {
            LockFilePath = lockFilePath;
            _lockStream = lockStream;
        }

        /// <summary>
        /// Attempts to acquire an exclusive lock for the given base directory.
        /// Throws if another process already holds the lock.
        /// </summary>
        public static DbInstanceLock Acquire(string baseDirectory, Action<string>? debugLog = null)
        {
            Directory.CreateDirectory(baseDirectory);

            var lockPath = Path.Combine(baseDirectory, ".amftpd.db.lock");

            try
            {
                // FileShare.None gives us an exclusive lock on this file.
                // DeleteOnClose ensures the lock file disappears when the process exits cleanly.
                var fs = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose | FileOptions.WriteThrough);

                // Optional: write some debug info (PID, timestamp)
                try
                {
                    using var writer = new StreamWriter(fs, Encoding.ASCII, leaveOpen: true);
                    writer.WriteLine($"pid={Environment.ProcessId}; started={DateTimeOffset.UtcNow:o}");
                    writer.Flush();
                }
                catch
                {
                    // best-effort; ignore
                }

                debugLog?.Invoke($"[DB-MANAGER] Acquired DB lock at '{lockPath}'.");

                return new DbInstanceLock(lockPath, fs);
            }
            catch (IOException ex)
            {
                debugLog?.Invoke($"[DB-MANAGER] Failed to acquire DB lock at '{lockPath}': {ex.Message}");
                throw new InvalidOperationException(
                    $"amFTPd DB directory '{baseDirectory}' is already in use by another process.", ex);
            }
        }

        public void Dispose()
        {
            try
            {
                _lockStream.Dispose();
            }
            catch
            {
                // ignore; nothing we can do
            }
        }
    }
}
