using System.Globalization;
using System.Text;

namespace amFTPd.Core;

internal sealed class FtpFileSystem
{
    private readonly string _rootFs; // physical root (full path)
    public FtpFileSystem(string rootFs) => _rootFs = Path.GetFullPath(rootFs);

    public string MapToPhysical(string virtualPath)
    {
        var rel = virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_rootFs, rel));
        if (!full.StartsWith(_rootFs, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        return full;
    }

    public string ToUnixListLine(FileSystemInfo fsi)
    {
        // -rw-r--r-- 1 owner group size Month Day HH:mm name
        var sb = new StringBuilder();
        bool isDir = (fsi.Attributes & FileAttributes.Directory) != 0;
        sb.Append(isDir ? 'd' : '-');
        sb.Append("rw-r--r-- 1 owner group ");
        long size = isDir ? 0 : (fsi is FileInfo fi ? fi.Length : 0);
        sb.Append(size.ToString(CultureInfo.InvariantCulture)).Append(' ');
        var dt = fsi.LastWriteTime;
        sb.Append(dt.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)).Append(' ');
        sb.Append(fsi.Name);
        return sb.ToString();
    }
}