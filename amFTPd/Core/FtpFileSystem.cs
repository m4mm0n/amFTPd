/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
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

using System.Globalization;
using System.Text;

namespace amFTPd.Core;

/// <summary>
/// Represents a virtual file system backed by a physical directory, providing methods to map virtual paths to physical
/// paths and generate Unix-style directory listings.
/// </summary>
/// <remarks>This class is designed to facilitate operations on a virtual file system rooted in a specific
/// physical directory. It ensures that all mapped paths remain within the bounds of the root directory for security
/// purposes. Additionally, it provides functionality to format file system entries in a Unix-style listing
/// format.</remarks>
public sealed class FtpFileSystem
{
    private readonly string _rootFs; // physical root (full path)
    /// <summary>
    /// Initializes a new instance of the <see cref="FtpFileSystem"/> class with the specified root file system path.
    /// </summary>
    /// <param name="rootFs">The root file system path to be used as the base directory for FTP operations. This path is resolved to its full
    /// path.</param>
    public FtpFileSystem(string rootFs) => _rootFs = Path.GetFullPath(rootFs);
    /// <summary>
    /// Maps a virtual path to its corresponding physical file system path.
    /// </summary>
    /// <remarks>This method ensures that the resolved physical path is within the root file system directory
    /// to prevent unauthorized access to files outside the intended directory structure.</remarks>
    /// <param name="virtualPath">The virtual path to be mapped. The path should start with a forward slash ('/').</param>
    /// <returns>The physical file system path corresponding to the specified virtual path.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the resolved physical path is outside the root file system directory.</exception>
    public string MapToPhysical(string virtualPath)
    {
        var rel = virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_rootFs, rel));
        return !full.StartsWith(_rootFs, StringComparison.Ordinal) ? throw new UnauthorizedAccessException() : full;
    }
    /// <summary>
    /// Converts the specified <see cref="FileSystemInfo"/> object into a Unix-style file listing line.
    /// </summary>
    /// <remarks>The returned string follows the format:  <c>-rw-r--r-- 1 owner group size Month Day HH:mm
    /// name</c>, where: <list type="bullet"> <item><description>The first character indicates whether the item is a
    /// directory (<c>d</c>) or a file (<c>-</c>).</description></item> <item><description>The size is <c>0</c> for
    /// directories.</description></item> <item><description>The date and time are formatted using the "MMM dd HH:mm"
    /// pattern in the invariant culture.</description></item> </list></remarks>
    /// <param name="fsi">The <see cref="FileSystemInfo"/> object representing the file or directory to convert.</param>
    /// <returns>A string representing the Unix-style file listing line, including permissions, owner, group, size, last modified
    /// date, and name.</returns>
    public string ToUnixListLine(FileSystemInfo fsi)
    {
        // -rw-r--r-- 1 owner group size Month Day HH:mm name
        var sb = new StringBuilder();
        var isDir = (fsi.Attributes & FileAttributes.Directory) != 0;
        sb.Append(isDir ? 'd' : '-');
        sb.Append("rw-r--r-- 1 owner group ");
        var size = isDir ? 0 : (fsi is FileInfo fi ? fi.Length : 0);
        sb.Append(size.ToString(CultureInfo.InvariantCulture)).Append(' ');
        var dt = fsi.LastWriteTime;
        sb.Append(dt.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)).Append(' ');
        sb.Append(fsi.Name);
        return sb.ToString();
    }
    /// <summary>
    /// Converts the specified <see cref="FileSystemInfo"/> object into an MLSD (Machine-Readable List Directory) line
    /// as defined in RFC 3659.
    /// </summary>
    /// <param name="fsi">The <see cref="FileSystemInfo"/> object representing the file or directory to be converted.</param>
    /// <returns>A string representing the MLSD line for the specified file or directory, including attributes such as
    /// type, modification time, size, and permissions.</returns>
    /// <remarks>
    /// The generated MLSD line includes the following attributes:
    /// - <c>type</c>: Indicates whether the entry is a file or directory.
    /// - <c>modify</c>: The last modification time in UTC, formatted as YYYYMMDDHHMMSS.
    /// - <c>size</c>: The size of the file in bytes (only for files).
    /// - <c>perm</c>: Permissions, where directories have "el" (enter, list) and files have "rl" (read, list).
    /// The name of the file or directory is appended at the end of the line.
    /// </remarks>
    public string ToMlsdLine(FileSystemInfo fsi)
    {
        var isDir = (fsi.Attributes & FileAttributes.Directory) != 0;

        var sb = new StringBuilder();

        // type
        sb.Append("type=");
        sb.Append(isDir ? "dir" : "file");
        sb.Append(';');

        // modify (UTC, RFC-3659 style YYYYMMDDHHMMSS)
        var dt = fsi.LastWriteTimeUtc;
        sb.Append("modify=");
        sb.Append(dt.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        sb.Append(';');

        // size (for files only)
        if (!isDir && fsi is FileInfo fi)
        {
            sb.Append("size=");
            sb.Append(fi.Length.ToString(CultureInfo.InvariantCulture));
            sb.Append(';');
        }

        // very simple permissions model: dirs = el, files = rl
        sb.Append(isDir ? "perm=el;" : "perm=rl;");

        sb.Append(' ');
        sb.Append(fsi.Name);

        return sb.ToString();
    }
}