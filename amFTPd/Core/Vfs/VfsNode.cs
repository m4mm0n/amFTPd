/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-28
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

namespace amFTPd.Core.Vfs
{
    /// <summary>
    /// Represents a node in a virtual file system, which can be a file or directory, and provides metadata about its
    /// type, paths, and content.
    /// </summary>
    /// <param name="Type">The type of the node, indicating whether it is a file or directory.</param>
    /// <param name="VirtualPath">The virtual path of the node within the virtual file system. This value cannot be null.</param>
    /// <param name="PhysicalPath">The physical path of the node on the underlying file system, if applicable. This value may be null for purely
    /// virtual nodes.</param>
    /// <param name="FileSystemInfo">The <see cref="FileSystemInfo"/> object associated with the node, providing additional metadata about the
    /// physical file or directory. This value may be null if the node does not correspond to a physical file system
    /// entity.</param>
    /// <param name="VirtualContent">The content of the node, if it is a virtual file. This value may be null for directories or nodes without
    /// virtual content.</param>
    public sealed record VfsNode(
        VfsNodeType Type,
        string VirtualPath,
        string? PhysicalPath,
        FileSystemInfo? FileSystemInfo,
        string? VirtualContent
    );
}
