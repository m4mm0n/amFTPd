using amFTPd.Config.Vfs;

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
    private readonly VfsConfig _config;
    private readonly FtpFileSystem _fs;
    private readonly VfsCache _cache;

    private readonly StringComparer _pathComparer;

    private readonly List<VfsMount> _mounts;
    private readonly List<VfsUserMount> _userMounts;
    private readonly List<VfsVirtualFile> _virtualFiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="VfsManager"/> class, which manages a virtual file system (VFS)
    /// with configurable mounts, user-specific mounts, and virtual files.
    /// </summary>
    /// <remarks>The <see cref="VfsManager"/> class is responsible for managing the virtual file
    /// system, including handling mounts, user-specific mounts, and virtual files. It uses the provided
    /// configuration to initialize its behavior, such as case sensitivity and cache time-to-live (TTL). The virtual
    /// file system is built on top of the provided FTP file system.</remarks>
    /// <param name="config">The configuration settings for the virtual file system, including cache settings, case sensitivity, and
    /// predefined mounts.</param>
    /// <param name="fs">The underlying FTP file system used to interact with the physical file system.</param>
    public VfsManager(VfsConfig config, FtpFileSystem fs)
    {
        _config = config;
        _fs = fs;
        _cache = new VfsCache(TimeSpan.FromSeconds(_config.CacheTtlSeconds));

        _pathComparer = _config.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        _mounts = new List<VfsMount>(config.Mounts);
        _userMounts = new List<VfsUserMount>(config.UserMounts);
        _virtualFiles = new List<VfsVirtualFile>(config.VirtualFiles);
    }

    public VfsResolveResult Resolve(string userName, string cwd, string path)
    {
        var virtPath = NormalizeVirtualPath(cwd, path);

        // Virtual file?
        var vfile = _virtualFiles.FirstOrDefault(v =>
            _pathComparer.Equals(v.VirtualPath, virtPath));

        if (vfile is not null)
        {
            var node_ = new VfsNode(
                VfsNodeType.VirtualFile,
                virtPath,
                null,
                null,
                vfile.StaticContent);
            return VfsResolveResult.Ok(node_);
        }

        // User mount?
        var userMount = _userMounts
            .Where(m => _pathComparer.Equals(m.UserName, userName))
            .OrderByDescending(m => m.VirtualPath.Length)
            .FirstOrDefault(m => virtPath.StartsWith(m.VirtualPath, _config.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

        string? physicalRoot = null;
        string? tail = null;

        if (userMount is not null)
        {
            physicalRoot = userMount.PhysicalPath;
            tail = virtPath[userMount.VirtualPath.Length..].TrimStart('/');
        }
        else
        {
            // global mount
            var mount = _mounts
                .OrderByDescending(m => m.VirtualPath.Length)
                .FirstOrDefault(m => virtPath.StartsWith(m.VirtualPath, _config.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

            if (mount is not null)
            {
                physicalRoot = mount.PhysicalPath;
                tail = virtPath[mount.VirtualPath.Length..].TrimStart('/');
            }
        }

        string physicalPath;
        if (physicalRoot is not null)
            physicalPath = Path.Combine(physicalRoot, tail ?? string.Empty);
        else
        {
            // Fallback to original MapToPhysical (user-root confinement)
            try
            {
                physicalPath = _fs.MapToPhysical(virtPath);
            }
            catch
            {
                return VfsResolveResult.Denied("550 Permission denied.\r\n");
            }
        }

        // Policy checks
        var ext = Path.GetExtension(physicalPath);
        if (_config.DenyExtensions.Any(e => _pathComparer.Equals(e, ext)))
            return VfsResolveResult.Denied("550 Access denied.\r\n");

        FileSystemInfo? fsi = null;

        if (_cache.TryGet(physicalPath, out var cached))
        {
            fsi = cached;
        }
        else
        {
            if (Directory.Exists(physicalPath))
                fsi = new DirectoryInfo(physicalPath);
            else if (File.Exists(physicalPath))
                fsi = new FileInfo(physicalPath);
            else
                return VfsResolveResult.NotFound("550 Not found.\r\n");

            _cache.Set(physicalPath, fsi);
        }

        if (_config.DenyHiddenFiles && (fsi.Attributes & FileAttributes.Hidden) != 0)
            return VfsResolveResult.Denied("550 Access denied.\r\n");

        if (fsi is FileInfo fi && _config.MaxFileSizeBytes > 0 && fi.Length > _config.MaxFileSizeBytes)
            return VfsResolveResult.Denied("552 File size exceeds policy.\r\n");

        var type = (fsi.Attributes & FileAttributes.Directory) != 0
            ? VfsNodeType.PhysicalDirectory
            : VfsNodeType.PhysicalFile;

        var node = new VfsNode(type, virtPath, physicalPath, fsi, null);
        return VfsResolveResult.Ok(node);
    }

    public string NormalizeVirtualPath(string cwd, string path)
    {
        var p = string.IsNullOrWhiteSpace(path) ? "." : path.Trim();

        if (!p.StartsWith('/'))
            p = $"{cwd.TrimEnd('/')}/{p}";

        var parts = new List<string>();
        foreach (var segment in p.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(segment);
        }

        return "/" + string.Join('/', parts);
    }
}