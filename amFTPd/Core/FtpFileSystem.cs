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
internal sealed class FtpFileSystem
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
        if (!full.StartsWith(_rootFs, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        return full;
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
}