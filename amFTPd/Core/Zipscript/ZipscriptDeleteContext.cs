/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptDeleteContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 09:03:41
 *  Last Modified:  2025-12-14 21:43:33
 *  CRC32:          0xB42313B4
 *  
 *  Description:
 *      Context passed when a file or directory is deleted.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Core.Zipscript;

/// <summary>
/// Context passed when a file or directory is deleted.
/// </summary>
/// <param name="SectionName">The logical section name the path belongs to.</param>
/// <param name="VirtualPath">Virtual path (FTP path) that was deleted.</param>
/// <param name="PhysicalPath">Underlying physical path on disk.</param>
/// <param name="IsDirectory">Whether the deleted item was a directory.</param>
/// <param name="UserName">User that performed the delete, if known.</param>
/// <param name="DeletedAt">Timestamp of the delete operation.</param>
public sealed record ZipscriptDeleteContext(
    string SectionName,
    string VirtualPath,
    string? PhysicalPath,
    bool IsDirectory,
    string? UserName,
    DateTimeOffset DeletedAt
);