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

using System.Text;

namespace amFTPd.Config.Vfs;

/// <summary>
/// Describes a single virtual file exposed by the FTP server.
/// This is typically used for things like MOTD, rules.txt, etc.
/// </summary>
public sealed record VfsVirtualFile
{
    /// <summary>
    /// Full virtual path as seen by the client, e.g. "/_welcome.txt".
    /// </summary>
    public string VirtualPath { get; init; } = "/";

    /// <summary>
    /// MIME type of the content. Defaults to "text/plain".
    /// </summary>
    public string ContentType { get; init; } = "text/plain";

    /// <summary>
    /// File content as UTF-8 text.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Last modified timestamp of the virtual file.
    /// </summary>
    public DateTimeOffset LastModified { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns the content encoded as UTF-8 bytes.
    /// </summary>
    public byte[] GetBytes() => Encoding.UTF8.GetBytes(Content);
}