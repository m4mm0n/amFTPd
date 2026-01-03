/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           VfsResolveResult.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xA4F5ED93
 *  
 *  Description:
 *      Represents the result of resolving a virtual file system (VFS) path.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Vfs;

/// <summary>
/// Represents the result of resolving a virtual file system (VFS) path.
/// </summary>
/// <remarks>This type encapsulates the outcome of a VFS path resolution operation, including whether the
/// resolution was successful, an optional error message, and the resolved node if applicable. It also provides factory
/// methods for common resolution outcomes, such as "Not Found," "Denied," and "Ok."</remarks>
/// <param name="Success">Indicates whether the resolution operation was successful.  <see langword="true"/> if the resolution succeeded;
/// otherwise, <see langword="false"/>.</param>
/// <param name="ErrorMessage">An optional error message describing the reason for a failed resolution. This value is <see langword="null"/> if the
/// resolution was successful.</param>
/// <param name="Node">The resolved <see cref="VfsNode"/> if the resolution was successful; otherwise, <see langword="null"/>.</param>
public sealed record VfsResolveResult(
    bool Success,
    string? ErrorMessage,
    VfsNode? Node
)
{
    /// <summary>
    /// Section resolved from the virtual path (FtpSection-based).
    /// </summary>
    public FtpSection? Section { get; init; }
    /// <summary>
    /// Creates a result indicating that the requested item was not found.
    /// </summary>
    /// <param name="msg">An optional message providing additional context about the result. If null, a default message "Not found." is
    /// used.</param>
    /// <returns>A <see cref="VfsResolveResult"/> instance representing a "not found" result.</returns>
    public static VfsResolveResult NotFound(string? msg = null)
        => new(false, msg ?? "Not found.", null);
    /// <summary>
    /// Creates a result indicating that access is denied.
    /// </summary>
    /// <param name="msg">An optional message describing the reason for the denial. If not provided, defaults to "Permission denied."</param>
    /// <returns>A <see cref="VfsResolveResult"/> instance representing a denied access result.</returns>
    public static VfsResolveResult Denied(string? msg = null)
        => new(false, msg ?? "Permission denied.", null);
    /// <summary>
    /// Creates a successful result for a virtual file system operation.
    /// </summary>
    /// <param name="node">The <see cref="VfsNode"/> associated with the successful operation.</param>
    /// <returns>A <see cref="VfsResolveResult"/> indicating a successful operation, containing the specified <paramref
    /// name="node"/>.</returns>
    public static VfsResolveResult Ok(VfsNode node)
        => new(true, null, node);
}