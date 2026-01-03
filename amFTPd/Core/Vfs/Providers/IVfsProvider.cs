using amFTPd.Config.Ftpd;
using System.Xml.Linq;

namespace amFTPd.Core.Vfs.Providers;

/// <summary>
/// Defines the contract for a virtual file system (VFS) provider that can resolve and enumerate virtual paths.
/// </summary>
/// <remarks>Implementations of this interface enable support for custom file system backends in a virtualized
/// environment. Each provider determines which paths it can handle and is responsible for resolving those paths and
/// enumerating directory contents as appropriate.</remarks>
public interface IVfsProvider
{
    /// <summary>
    /// Fast path check: does this provider care about this path?
    /// </summary>
    bool CanHandle(string virtualPath);

    /// <summary>
    /// Resolve a path to a node.
    /// </summary>
    VfsResolveResult Resolve(string virtualPath, FtpUser? user);

    /// <summary>
    /// Enumerate children of a directory (LIST/NLST).
    /// Return empty if not applicable.
    /// </summary>
    IEnumerable<VfsNode> Enumerate(string virtualPath, FtpUser? user);
}