/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           Lz4Codec.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 19:45:27
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x4197ED4C
 *  
 *  Description:
 *      Provides methods for compressing and decompressing data using a lightweight LZ4-like block compression algorithm.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

using K4os.Compression.LZ4;

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
        private const byte RAW = 0x00;
        private const byte LZ4 = 0x01;

        // Minimum size where compression makes sense
        private const int MinCompressSize = 64;

        public static byte[] Compress(byte[] src)
        {
            if (src == null)
                throw new ArgumentNullException(nameof(src));

            if (src.Length < MinCompressSize)
                return MakeRaw(src);

            var maxSize = LZ4Codec.MaximumOutputSize(src.Length);
            var compressed = new byte[maxSize];

            var compressedSize = LZ4Codec.Encode(
                src, 0, src.Length,
                compressed, 0, compressed.Length,
                LZ4Level.L00_FAST);

            // If compression is pointless, store raw
            if (compressedSize <= 0 || compressedSize >= src.Length)
                return MakeRaw(src);

            using var ms = new MemoryStream(1 + 4 + 4 + compressedSize);
            using var bw = new BinaryWriter(ms);

            bw.Write(LZ4);
            bw.Write(src.Length);
            bw.Write(compressedSize);
            bw.Write(compressed, 0, compressedSize);

            return ms.ToArray();
        }

        public static byte[] Decompress(byte[] src)
        {
            if (src == null)
                throw new ArgumentNullException(nameof(src));
            if (src.Length == 0)
                return Array.Empty<byte>();

            var mode = src[0];

            if (mode == RAW)
            {
                var raw = new byte[src.Length - 1];
                Buffer.BlockCopy(src, 1, raw, 0, raw.Length);
                return raw;
            }

            if (mode != LZ4)
                throw new InvalidDataException($"Unknown compression mode: {mode}");

            using var ms = new MemoryStream(src, 1, src.Length - 1);
            using var br = new BinaryReader(ms);

            var originalLength = br.ReadInt32();
            var compressedLength = br.ReadInt32();

            if (originalLength < 0 || compressedLength < 0)
                throw new InvalidDataException("Invalid LZ4 header");

            var compressed = br.ReadBytes(compressedLength);
            if (compressed.Length != compressedLength)
                throw new InvalidDataException("Truncated LZ4 payload");

            var output = new byte[originalLength];

            var decoded = LZ4Codec.Decode(
                compressed, 0, compressed.Length,
                output, 0, output.Length);

            if (decoded != originalLength)
                throw new InvalidDataException("LZ4 decompression size mismatch");

            return output;
        }

        private static byte[] MakeRaw(byte[] src)
        {
            var dst = new byte[src.Length + 1];
            dst[0] = RAW;
            Buffer.BlockCopy(src, 0, dst, 1, src.Length);
            return dst;
        }
    }
}
