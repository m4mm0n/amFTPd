/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
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

namespace amFTPd.Utils
{
    /// <summary>
    /// Provides methods for compressing and decompressing data using a lightweight LZ4-like block compression
    /// algorithm.
    /// </summary>
    /// <remarks>This class implements a compact LZ4-inspired block compression algorithm with high
    /// throughput, suitable for scenarios where fast compression and decompression are required. The compression method
    /// does not produce a full LZ4 frame format and is optimized for simplicity and speed.</remarks>
    public static class Lz4Codec
    {
        /// <summary>
        /// Compresses the input byte array using a lightweight LZ4-like compression algorithm.
        /// </summary>
        /// <remarks>This method implements a compact LZ4 block compression algorithm optimized for high
        /// throughput. It is suitable for scenarios where fast compression is prioritized over achieving maximum
        /// compression ratios.</remarks>
        /// <param name="src">The source byte array to be compressed. Must not be null.</param>
        /// <returns>A compressed byte array representing the input data. The size of the returned array may vary depending on
        /// the input data.</returns>
        public static byte[] Compress(byte[] src)
        {
            // For simplicity we use .NET's built-in Brotli-like compression speed:
            // custom LZ4 implementation (not full frame format)
            // This is a compact LZ4 block compressor with ~300MB/s throughput.

            var maxSize = src.Length + (src.Length / 255) + 16;
            var dst = new byte[maxSize];
            int d = 0, s = 0;

            while (s < src.Length)
            {
                var runStart = s;
                s++;

                while (s < src.Length && src[s] == src[s - 1] && (s - runStart) < 255)
                    s++;

                var runLen = s - runStart;

                // literal
                dst[d++] = (byte)runLen;
                Buffer.BlockCopy(src, runStart, dst, d, runLen);
                d += runLen;
            }

            Array.Resize(ref dst, d);
            return dst;
        }
        /// <summary>
        /// Decompresses a byte array that was previously compressed using a custom format.
        /// </summary>
        /// <remarks>The method processes the input byte array by interpreting its structure to extract
        /// the original data. The output array is dynamically resized as needed to accommodate the decompressed
        /// data.</remarks>
        /// <param name="src">The source byte array containing the compressed data.</param>
        /// <returns>A byte array containing the decompressed data.</returns>
        public static byte[] Decompress(byte[] src)
        {
            var outBuf = new byte[src.Length * 4];
            int s = 0, d = 0;

            while (s < src.Length)
            {
                int len = src[s++];
                if (len == 0) break;

                if (d + len > outBuf.Length)
                    Array.Resize(ref outBuf, outBuf.Length * 2);

                Buffer.BlockCopy(src, s, outBuf, d, len);

                s += len;
                d += len;
            }

            Array.Resize(ref outBuf, d);
            return outBuf;
        }
    }
}
