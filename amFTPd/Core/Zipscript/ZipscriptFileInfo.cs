/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptFileInfo.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 04:34:01
 *  Last Modified:  2025-12-14 09:02:08
 *  CRC32:          0x958EE0A1
 *  
 *  Description:
 *      Represents information about a file processed by a Zipscript operation, including its name, size, CRC values, and pro...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
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
        /// <summary>
        /// Gets the name of the file associated with this instance.
        /// </summary>
        public string FileName { get; init; } = string.Empty;
        /// <summary>
        /// Gets or sets the expected CRC (Cyclic Redundancy Check) value for data validation.
        /// </summary>
        public uint? ExpectedCrc { get; set; }
        /// <summary>
        /// Gets or sets the actual CRC (Cyclic Redundancy Check) value calculated for the associated data.
        /// </summary>
        public uint? ActualCrc { get; set; }
        /// <summary>
        /// Gets or sets the size, in bytes.
        /// </summary>
        public long SizeBytes { get; set; }
        /// <summary>
        /// Gets or sets the current processing state of the Zipscript file.
        /// </summary>
        public ZipscriptFileState State { get; set; }
        /// <summary>Creation timestamp for this file from the zipscript’s perspective.</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>Last time this file was updated (upload/verify/delete/nuke).</summary>
        public DateTimeOffset LastUpdatedAt { get; set; }

        /// <summary>Whether this file is part of a nuked release.</summary>
        public bool IsNuked { get; set; }

        /// <summary>Optional reason string for the nuke.</summary>
        public string? NukeReason { get; set; }

        /// <summary>User who nuked the release, if known.</summary>
        public string? NukedBy { get; set; }

        /// <summary>Timestamp when the nuke was applied.</summary>
        public DateTimeOffset? NukedAt { get; set; }
    }
}
