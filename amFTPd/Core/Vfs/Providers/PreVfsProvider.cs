using amFTPd.Config.Ftpd;
using amFTPd.Core.Pre;
using amFTPd.Core.Vfs.Virtual;

namespace amFTPd.Core.Vfs.Providers;

/// <summary>
/// Provides a virtual file system (VFS) provider for handling paths under the "/PRE" namespace using the PRE registry.
/// </summary>
/// <remarks>This provider is intended for use with virtual paths that begin with "/PRE" and delegates resolution
/// to the PRE registry. It implements the IVfsProvider interface to support integration with systems that require
/// virtual file system abstraction.</remarks>
public sealed class PreVfsProvider : IVfsProvider
{
    private readonly PreVirtualDirectory _pre;

    public PreVfsProvider(PreRegistry pres)
    {
        _pre = new PreVirtualDirectory(pres);
    }

    public bool CanHandle(string virtualPath)
        => Normalize(virtualPath)
            .StartsWith("/PRE", StringComparison.OrdinalIgnoreCase);

    public VfsResolveResult Resolve(string virtualPath, FtpUser? user)
        => _pre.Resolve(virtualPath, user);

    public IEnumerable<VfsNode> Enumerate(string virtualPath, FtpUser? user)
        => _pre.Enumerate(virtualPath);

    private static string Normalize(string path)
    {
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');
        return path;
    }
}