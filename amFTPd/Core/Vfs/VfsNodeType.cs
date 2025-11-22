namespace amFTPd.Core.Vfs;

/// <summary>
/// Specifies the type of a node in a virtual file system (VFS).
/// </summary>
/// <remarks>This enumeration distinguishes between physical and virtual nodes, as well as files and
/// directories. It is commonly used to identify the nature of a node when interacting with a virtual file
/// system.</remarks>
public enum VfsNodeType
{
    PhysicalFile,
    PhysicalDirectory,
    VirtualFile,
    VirtualDirectory
}