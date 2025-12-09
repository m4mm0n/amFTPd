/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           VfsNodeType.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x91EC343A
 *  
 *  Description:
 *      Specifies the type of a node in a virtual file system (VFS).
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core.Vfs;

/// <summary>
/// Specifies the type of a node in a virtual file system (VFS).
/// </summary>
/// <remarks>This enumeration distinguishes between physical and virtual nodes, as well as files and
/// directories. It is commonly used to identify the nature of a node when interacting with a virtual file
/// system.</remarks>
public enum VfsNodeType
{
    PhysicalFile,
    PhysicalDirectory,
    VirtualFile,
    VirtualDirectory
}