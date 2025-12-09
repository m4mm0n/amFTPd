/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           VfsUserMount.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xE4BAFF66
 *  
 *  Description:
 *      User-specific VFS mount.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Config.Vfs;

/// <summary>
/// User-specific VFS mount.
/// </summary>
public sealed record VfsUserMount
{
    public string UserName { get; init; } = string.Empty;

    /// <summary>Compatibility alias if any code uses "Username".</summary>
    public string Username
    {
        get => UserName;
        init => UserName = value;
    }

    public string VirtualPath { get; init; } = "/";
    public string PhysicalPath { get; init; } = "/";
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Name of the global mount this user mount refers to (from config).
    /// </summary>
    public string? MountName { get; init; }

    /// <summary>
    /// Resolved global mount; this is what VfsConfig uses (VirtualRoot, etc).
    /// </summary>
    public VfsMount? Mount { get; init; }

    public VfsUserMount()
    {
    }

    public VfsUserMount(
        string userName,
        string virtualPath,
        string physicalPath,
        bool isReadOnly = false,
        string? mount = null)      // 'mount' is the mount name from config
    {
        UserName = userName;
        VirtualPath = virtualPath;
        PhysicalPath = physicalPath;
        IsReadOnly = isReadOnly;
        MountName = mount;
        // Mount stays null here; it should be wired later by VfsConfig / loader.
    }

    public override string ToString()
        => $"{UserName}@{VirtualPath} -> {PhysicalPath}";
}