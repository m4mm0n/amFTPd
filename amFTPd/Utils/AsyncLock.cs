/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AsyncLock.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 01:11:14
 *  Last Modified:  2025-12-14 01:13:35
 *  CRC32:          0x9FFB4D98
 *  
 *  Description:
 *      Lightweight async-compatible lock built on <see cref="SemaphoreSlim"/>.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Utils
{
    /// <summary>
    /// Lightweight async-compatible lock built on <see cref="SemaphoreSlim"/>.
    /// </summary>
    public sealed class AsyncLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _disposed;

        public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(_semaphore);
        }

        public IDisposable Lock()
        {
            ThrowIfDisposed();
            _semaphore.Wait();
            return new Releaser(_semaphore);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncLock));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Dispose();
        }

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim? _toRelease;

            public Releaser(SemaphoreSlim toRelease)
            {
                _toRelease = toRelease;
            }

            public void Dispose()
            {
                var s = Interlocked.Exchange(ref _toRelease, null);
                if (s is not null)
                    s.Release();
            }
        }
    }
}
