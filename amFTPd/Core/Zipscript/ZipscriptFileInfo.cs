/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02
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

namespace amFTPd.Core.Zipscript
{
    /// <summary>
    /// Represents information about a file processed by a Zipscript operation, including its name, size, CRC values,
    /// and processing state.
    /// </summary>
    /// <remarks>This class provides details about a file's metadata and its state during or after a Zipscript
    /// operation.  It includes properties for the file name, size in bytes, expected and actual CRC values, and the
    /// current processing state.</remarks>
    public sealed class ZipscriptFileInfo
    {
        public string FileName { get; init; } = string.Empty;
        public uint? ExpectedCrc { get; set; }
        public uint? ActualCrc { get; set; }
        public long SizeBytes { get; set; }
        public ZipscriptFileState State { get; set; }
    }
}
