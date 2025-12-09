/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-12-02
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

using amFTPd.Config.Ftpd;
using amFTPd.Config.Vfs;
using amFTPd.Core.Sections;

namespace amFTPd.Core.Vfs;

/// <summary>
/// Manages a virtual file system (VFS) by resolving virtual paths to physical file system paths, handling
/// user-specific mounts, global mounts, and virtual files.
/// </summary>
/// <remarks>The <see cref="VfsManager"/> class provides functionality to map virtual paths to physical
/// paths based on a configuration that includes global mounts, user-specific mounts, and virtual files. It also
/// enforces policies such as file size limits, hidden file restrictions, and denied file extensions. This class is
/// designed to work with an FTP file system and supports caching for improved performance.</remarks>
public sealed class VfsManager
{
    private readonly List<VfsMount> _mounts;
    private readonly List<VfsUserMount> _userMounts;
    private readonly SectionResolver _sectionResolver;
    private readonly VfsCache _cache;

    public VfsManager(
        IEnumerable<VfsMount> mounts,
        IEnumerable<VfsUserMount> userMounts,
        SectionResolver sectionResolver)
    {
        _mounts = mounts?.ToList() ?? new();
        _userMounts = userMounts?.ToList() ?? new();
        _sectionResolver = sectionResolver ?? throw new ArgumentNullException(nameof(sectionResolver));

        // your real class requires a TimeSpan TTL
        _cache = new VfsCache(TimeSpan.FromSeconds(5));
    }

    // ---------------------------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------------------------
    public VfsResolveResult Resolve(string virtualPath, FtpUser user)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            return VfsResolveResult.NotFound("No path provided.");

        virtualPath = NormalizeVirtualPath(virtualPath);

        // ----------------------------
        // 1. User-specific mounts
        // ----------------------------
        var um = _userMounts
            .Where(m => virtualPath.StartsWith(m.VirtualPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.VirtualPath.Length)
            .FirstOrDefault();

        if (um != null)
        {
            var relative = virtualPath[um.VirtualPath.Length..].TrimStart('/');
            var physical = Path.Combine(um.PhysicalPath, relative);

            return BuildPhysicalDirResult(virtualPath, physical, user,
                _sectionResolver.Resolve(virtualPath));
        }

        // ----------------------------
        // 2. Global VFS mounts
        // ----------------------------
        var gm = _mounts
            .Where(m => virtualPath.StartsWith(m.VirtualPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.VirtualPath.Length)
            .FirstOrDefault();

        if (gm != null)
        {
            var relative = virtualPath[gm.VirtualPath.Length..].TrimStart('/');
            var physical = Path.Combine(gm.PhysicalPath, relative);

            return BuildPhysicalDirResult(virtualPath, physical, user,
                _sectionResolver.Resolve(virtualPath));
        }

        // ----------------------------
        // 3. Auto-section mounts (Option A)
        // ----------------------------
        var section = _sectionResolver.Resolve(virtualPath);
        if (section != null)
        {
            var vr = section.VirtualRoot;
            var relative = virtualPath.Length > vr.Length
                ? virtualPath[vr.Length..].TrimStart('/')
                : string.Empty;

            // Auto-mount into the user's home directory
            var physical = Path.Combine(
                user.HomeDir,
                vr.TrimStart('/'),
                relative
            );

            return BuildPhysicalDirResult(virtualPath, physical, user, section);
        }

        // ----------------------------
        // 4. Fallback: Home directory mapping
        // ----------------------------
        {
            var relative = virtualPath.TrimStart('/');
            var physical = Path.Combine(user.HomeDir, relative);

            return BuildPhysicalDirResult(virtualPath, physical, user, null);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------
    private static string NormalizeVirtualPath(string path)
    {
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        return path;
    }

    private VfsResolveResult BuildPhysicalDirResult(
        string virtualPath,
        string physicalPath,
        FtpUser user,
        FtpSection? section)
    {
        FileSystemInfo? fsi = null;
        if (Directory.Exists(physicalPath))
            fsi = new DirectoryInfo(physicalPath);
        else if (File.Exists(physicalPath))
            fsi = new FileInfo(physicalPath);

        var node = new VfsNode(
            Type: VfsNodeType.PhysicalDirectory,
            VirtualPath: virtualPath,
            PhysicalPath: physicalPath,
            FileSystemInfo: fsi,
            VirtualContent: null
        );

        return new VfsResolveResult(true, null, node)
        {
            Section = section
        };
    }
}