/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25
 *  Last Modified:  2025-12-02
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

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace amFTPd.Utils
{
    /// <summary>
    /// Provides methods for computing CRC32 checksums for data, streams, and files.
    /// </summary>
    /// <remarks>This class includes single-shot, streaming, and incremental APIs for CRC32 computation, as
    /// well as utilities for parallel processing and formatting CRC32 values. It uses the IEEE 802.3 polynomial
    /// (reflected) for compatibility with SFV tools.</remarks>
    public static unsafe class Crc32
    {
        // IEEE 802.3 polynomial (reflected) — SFV-compatible
        private const uint Polynomial = 0xEDB88320u;

        // 256-entry lookup table
        private static readonly uint[] Table = CreateTable();

        // Buffer size used for streaming APIs
        public const int DefaultBufferSize = 64 * 1024;

        static uint[] CreateTable()
        {
            var table = new uint[256];

            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (var bit = 0; bit < 8; bit++)
                {
                    c = (c & 1) != 0
                        ? (Polynomial ^ (c >> 1))
                        : (c >> 1);
                }

                table[i] = c;
            }

            return table;
        }

        static Crc32() => Table = CreateTable();

        // ------------------
        // Core single-shot APIs
        // ------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Compute(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            fixed (byte* p = data)
                return Finalize(ComputeInternal(Seed(), p, data.Length));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
                return Finalize(Seed());

            fixed (byte* p = &MemoryMarshal.GetReference(data))
                return Finalize(ComputeInternal(Seed(), p, data.Length));
        }

        // ------------------
        // Streaming APIs (good for huge files)
        // ------------------

        /// <summary>
        /// Computes CRC32 for a stream, using ArrayPool-backed buffer.
        /// </summary>
        public static uint Compute(Stream stream, int bufferSize = DefaultBufferSize, bool leaveOpen = false)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
            if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));

            var crc = Seed();
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    fixed (byte* p = buffer) crc = ComputeInternal(crc, p, read);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                if (!leaveOpen)
                    stream.Dispose();
            }

            return Finalize(crc);
        }

        /// <summary>
        /// Computes CRC32 for a file path using a pooled buffer.
        /// </summary>
        public static uint ComputeFile(string path, int bufferSize = DefaultBufferSize)
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Compute(fs, bufferSize, leaveOpen: false);
        }
        /// <summary>
        /// Computes a hash value for the contents of the specified file.
        /// </summary>
        /// <param name="path">The path to the file whose hash value is to be computed. Cannot be null or empty.</param>
        /// <param name="bufferSize">The size of the buffer, in bytes, used to read the file. Must be greater than zero. Defaults to the
        /// system-defined buffer size.</param>
        /// <returns>The computed hash value as an unsigned 32-bit integer.</returns>
        public static uint Compute(string path, int bufferSize = DefaultBufferSize)
        {
            using var fs = File.OpenRead(path);
            return Compute(fs, bufferSize, false);
        }

        // ------------------
        // Incremental / append APIs (for manual streaming)
        // ------------------

        /// <summary>
        /// Returns the initial CRC seed (for incremental use).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Seed() => 0xFFFFFFFFu;

        /// <summary>
        /// Finalizes an incremental CRC value (XOR with 0xFFFFFFFF).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Finalize(uint crc) => crc ^ 0xFFFFFFFFu;

        /// <summary>
        /// Continues an ongoing CRC with a new chunk of data.
        /// 
        /// You must start with Seed() and after last chunk call Finalize().
        /// </summary>
        public static uint Append(uint currentCrc, ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
                return currentCrc;

            fixed (byte* p = &MemoryMarshal.GetReference(data))
                return ComputeInternal(currentCrc, p, data.Length);
        }

        // ------------------
        // Parallel helpers (multi-file SFV-like checks)
        // ------------------

        /// <summary>
        /// Computes CRC32 for multiple files in parallel.
        /// </summary>
        public static IDictionary<string, uint> ComputeFilesParallel(
            IEnumerable<string> paths,
            int bufferSize = DefaultBufferSize,
            int? maxDegreeOfParallelism = null)
        {
            if (paths is null) throw new ArgumentNullException(nameof(paths));

            var results = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            object sync = new();

            Parallel.ForEach(
                paths,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount
                },
                path =>
                {
                    var crc = ComputeFile(path, bufferSize);
                    lock (sync)
                    {
                        results[path] = crc;
                    }
                });

            return results;
        }

        // ------------------
        // Formatting helpers (SFV-compatible hex)
        // ------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHex(uint crc) => crc.ToString("X8");

        // ------------------
        // Core unsafe loop
        // ------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeInternal(uint crc, byte* ptr, int length)
        {
            var p = ptr;
            var end = ptr + length;

            // Simple, branchless, table-driven core.
            while (p < end) crc = Table[(crc ^ *p++) & 0xFF] ^ (crc >> 8);

            return crc;
        }
    }
}
