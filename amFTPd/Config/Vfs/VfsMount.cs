/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-22
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

namespace amFTPd.Config.Vfs
{
    /// <summary>
    /// Represents a mapping between a virtual file system path and a physical file system path.
    /// </summary>
    /// <param name="VirtualPath">The virtual path in the file system. This path is used to reference files or directories in the virtual file
    /// system.</param>
    /// <param name="PhysicalPath">The physical path on the underlying file system that corresponds to the virtual path. This path must be a valid
    /// and accessible directory or file path.</param>
    public sealed record VfsMount(
        string VirtualPath,
        string PhysicalPath
    );
}
