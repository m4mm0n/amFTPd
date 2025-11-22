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

namespace amFTPd.Config.Vfs;

/// <summary>
/// Represents a user-specific virtual file system (VFS) mount, mapping a virtual path to a physical path.
/// </summary>
/// <param name="UserName">The name of the user associated with this mount. This value cannot be null or empty.</param>
/// <param name="VirtualPath">The virtual path exposed to the user. This value cannot be null or empty.</param>
/// <param name="PhysicalPath">The physical path on the file system that corresponds to the virtual path. This value cannot be null or empty.</param>
public sealed record VfsUserMount(
    string UserName,
    string VirtualPath,
    string PhysicalPath
);