using amFTPd.Config.Ftpd;
using amFTPd.Core.Sections;
using amFTPd.Core.Vfs.Virtual;

namespace amFTPd.Core.Vfs.Providers;

/// <summary>
/// Provides a virtual file system (VFS) provider that resolves and manages shortcut entries within the VFS namespace.
/// </summary>
/// <remarks>This provider handles virtual paths that represent shortcuts, where the path does not contain any
/// slashes. It is typically used to resolve and enumerate shortcut nodes for users of the VFS. The provider does not
/// support directory enumeration and will return an empty result for such operations.</remarks>
public sealed class ShortcutVfsProvider : IVfsProvider
{
    private readonly ShortcutVirtualDirectory _shortcuts;

    public ShortcutVfsProvider(SectionResolver resolver) => _shortcuts = new ShortcutVirtualDirectory(resolver);

    public bool CanHandle(string virtualPath)
    {
        if (virtualPath.Length < 2)
            return false;

        var name = virtualPath.Trim('/');

        // shortcut has no slashes
        return !name.Contains('/');
    }

    public VfsResolveResult Resolve(string virtualPath, FtpUser? user)
        => _shortcuts.Resolve(virtualPath);

    public IEnumerable<VfsNode> Enumerate(string virtualPath, FtpUser? user)
        => Enumerable.Empty<VfsNode>();
}