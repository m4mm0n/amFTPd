/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23
 *  Last Modified:  2025-11-23
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

namespace amFTPd.Core.Access
{
    /// <summary>
    /// Represents the effective access decision for a directory path:
    /// listing, uploading, and downloading.
    /// </summary>
    /// <param name="CanList">Whether directory listings are allowed.</param>
    /// <param name="CanUpload">Whether uploads are allowed.</param>
    /// <param name="CanDownload">Whether downloads are allowed.</param>
    public sealed record DirectoryAccess(
        bool CanList,
        bool CanUpload,
        bool CanDownload
    );
}
