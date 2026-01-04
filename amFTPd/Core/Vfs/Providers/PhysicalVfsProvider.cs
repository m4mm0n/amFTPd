using amFTPd.Config.Ftpd;
using amFTPd.Config.Vfs;
using amFTPd.Core.Sections;

namespace amFTPd.Core.Vfs.Providers;

/// <summary>
/// Provides a virtual file system (VFS) provider that resolves virtual paths to physical file system locations using
/// configured mounts, user-specific mounts, and user home directories.
/// </summary>
/// <remarks>This provider acts as a fallback VFS provider, handling any virtual path by mapping it to the
/// appropriate physical location based on user and global mount configurations. It supports user-specific mounts,
/// global mounts, and automatic resolution to user home directories when no explicit mount is found. This class is
/// typically used in scenarios where virtual file system paths need to be transparently mapped to the underlying file
/// system for FTP or similar services.</remarks>
public sealed class PhysicalVfsProvider : IVfsProvider
{
    private readonly IEnumerable<VfsMount> _mounts;
    private readonly IEnumerable<VfsUserMount> _userMounts;
    private readonly SectionResolver _sections;

    public PhysicalVfsProvider(
        IEnumerable<VfsMount> mounts,
        IEnumerable<VfsUserMount> userMounts,
        SectionResolver sections)
    {
        _mounts = mounts;
        _userMounts = userMounts;
        _sections = sections;
    }

    public bool CanHandle(string virtualPath)
        => true; // fallback provider

    public VfsResolveResult Resolve(string virtualPath, FtpUser? user)
    {
        // 1. User mounts
        var um = _userMounts
            .Where(m => virtualPath.StartsWith(m.VirtualPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.VirtualPath.Length)
            .FirstOrDefault();

        if (um != null)
            return ResolvePhysical(virtualPath, um.VirtualPath, um.PhysicalPath, user);

        // 2. Global mounts
        var gm = _mounts
            .Where(m => virtualPath.StartsWith(m.VirtualPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.VirtualPath.Length)
            .FirstOrDefault();

        if (gm != null)
            return ResolvePhysical(virtualPath, gm.VirtualPath, gm.PhysicalPath, user);

        // 3. Auto-section
        //var section = _sections.Resolve(virtualPath);
        //if (section != null && user?.HomeDir != null)
        //{
        //    var relative = virtualPath[section.VirtualRoot.Length..].TrimStart('/');
        //    var physical = Path.Combine(user.HomeDir, section.VirtualRoot.Trim('/'), relative);
        //    return BuildResult(virtualPath, physical, section);
        //}

        // 4. Home fallback
        if (user?.HomeDir != null)
        {
            var relative = virtualPath.TrimStart('/');
            var physical = SafeCombineUnderRoot(user.HomeDir, relative);
            if (physical is null)
                return VfsResolveResult.Denied();
            return BuildResult(virtualPath, physical, null);
        }

        return VfsResolveResult.NotFound();
    }



    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
            return path;

        return path + Path.DirectorySeparatorChar;
    }

    private static string? SafeCombineUnderRoot(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;

        var rootFull = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relative));

        // Allow the root itself as a valid resolved path.
        if (string.IsNullOrWhiteSpace(relative))
            return rootFull;

        var rootPrefix = EnsureTrailingSeparator(rootFull);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!combined.StartsWith(rootPrefix, comparison))
            return null;

        return combined;
    }
    public IEnumerable<VfsNode> Enumerate(string virtualPath, FtpUser? user) =>
        // Physical enumeration happens via DirectoryInfo elsewhere
        Enumerable.Empty<VfsNode>();

    private static VfsResolveResult ResolvePhysical(
        string virtualPath,
        string virtualRoot,
        string physicalRoot,
        FtpUser? user)
    {
        var relative = virtualPath[virtualRoot.Length..].TrimStart('/');
        var physical = SafeCombineUnderRoot(physicalRoot, relative);
        if (physical is null)
            return VfsResolveResult.Denied();
        return BuildResult(virtualPath, physical, null);
    }

    private static VfsResolveResult BuildResult(
        string virtualPath,
        string physicalPath,
        FtpSection? section)
    {
        FileSystemInfo? fsi = null;

        if (Directory.Exists(physicalPath))
            fsi = new DirectoryInfo(physicalPath);
        else if (File.Exists(physicalPath))
            fsi = new FileInfo(physicalPath);

        // If the resolved physical target does not exist, treat it as not found.
        // This allows later providers (e.g. section shortcuts) to respond.
        if (fsi is null)
            return VfsResolveResult.NotFound();

        var node = new VfsNode(
            fsi is FileInfo ? VfsNodeType.PhysicalFile : VfsNodeType.PhysicalDirectory,
            virtualPath,
            physicalPath,
            fsi,
            null);

        return new VfsResolveResult(true, null, node) { Section = section };
    }
}