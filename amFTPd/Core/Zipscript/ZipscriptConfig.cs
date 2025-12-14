/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 08:59:03
 *  Last Modified:  2025-12-14 10:56:22
 *  CRC32:          0xA7C9326B
 *  
 *  Description:
 *      Configuration for the internal zipscript engine.
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
    /// Configuration for the internal zipscript engine.
    /// </summary>
    public sealed class ZipscriptConfig
    {
        /// <summary>
        /// Enables or disables the zipscript engine entirely.
        /// </summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// Path to the zipscript DB snapshot file. If empty or null, a default path
        /// next to the user DB will be used.
        /// </summary>
        public string? DatabasePath { get; init; }

        /// <summary>
        /// If true, heavy checks (directory rescans) may be offloaded to background
        /// jobs in the future. Currently unused but reserved for compatibility.
        /// </summary>
        public bool OffloadHeavyChecks { get; init; }

        /// <summary>
        /// If true, we treat SFV mismatches more strictly (reserved for future use).
        /// </summary>
        public bool StrictSfv { get; init; }

        /// <summary>
        /// If true, RESCAN will recurse into subdirectories by default.
        /// </summary>
        public bool IncludeSubdirsOnRescan { get; init; }
    }
}
