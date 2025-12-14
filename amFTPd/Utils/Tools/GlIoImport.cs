/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           GlIoImport.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 18:07:11
 *  Last Modified:  2025-12-14 18:07:37
 *  CRC32:          0x117FFCAC
 *  
 *  Description:
 *      Helper for importing data from glFTPD/ioFTPD-style databases into amFTPd. The concrete file formats are intentionally...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using amFTPd.Config.Ftpd;
using amFTPd.Db;

namespace amFTPd.Utils.Tools
{
    /// <summary>
    /// Helper for importing data from glFTPD/ioFTPD-style databases into amFTPd.
    /// The concrete file formats are intentionally left implementation-specific.
    /// </summary>
    public static class GlIoImport
    {
        /// <summary>
        /// Imports users, groups, sections, stats and dupes from the given source root
        /// into the amFTPd database and configuration.
        /// </summary>
        /// <param name="sourceRoot">
        /// Root directory containing gl/io databases (userfiles, groups, logs, etc.).
        /// </param>
        /// <param name="db">
        /// Target amFTPd database manager.
        /// </param>
        /// <param name="sections">
        /// Section manager (to help map legacy section names to amFTPd sections/aliases).
        /// </param>
        public static async Task ImportAsync(
            string sourceRoot,
            DatabaseManager db,
            SectionManager sections,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
                throw new ArgumentException("Source root must not be empty.", nameof(sourceRoot));

            // TODO: Implement:
            //  - Read glFTPD/ioFTPD users & groups, map to amFTPd user schema.
            //  - Read legacy stats & dupes, feed into amFTPd stores (dupe DB, zipscript DB).
            //  - Use sections.FindByNameOrAlias to map sections by name.
            // For now we simply provide the scaffold.
            await Task.CompletedTask;
        }
    }
}
