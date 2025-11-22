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
/// Represents a virtual file in a virtual file system, with optional static content or dynamically generated
/// content.
/// </summary>
/// <param name="VirtualPath">The virtual path of the file within the virtual file system. This path uniquely identifies the file.</param>
/// <param name="StaticContent">The static content of the file, if available. If <see langword="null"/>, the content may be dynamically
/// generated.</param>
/// <param name="ScriptName">The name of the script or handler used to dynamically generate the content, if applicable. This value is
/// optional and may be <see langword="null"/>.</param>
public sealed record VfsVirtualFile(
    string VirtualPath,
    string? StaticContent,   // if null, dynamic script/handler may generate content
    string? ScriptName       // hook name in AMScript to generate content, optional
);