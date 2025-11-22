namespace amFTPd.Core.Vfs
{
    /// <summary>
    /// Represents a node in a virtual file system, which can be a file or directory, and provides metadata about its
    /// structure and content.
    /// </summary>
    /// <param name="Type">The type of the node, indicating whether it is a file or directory.</param>
    /// <param name="VirtualPath">The virtual path of the node within the virtual file system. This value is always non-null.</param>
    /// <param name="PhysicalPath">The physical path of the node on the underlying file system, if applicable. This value may be <see
    /// langword="null"/> for purely virtual nodes.</param>
    /// <param name="FileSystemInfo">The file system metadata associated with the node, such as attributes or timestamps, if available. This value
    /// may be <see langword="null"/> for virtual nodes without a physical counterpart.</param>
    /// <param name="VirtualContent">The content of the node, if it is a virtual file. This value is <see langword="null"/> for directories or nodes
    /// without virtual content.</param>
    public sealed record VfsNode(
        VfsNodeType Type,
        string VirtualPath,
        string? PhysicalPath,
        FileSystemInfo? FileSystemInfo,
        string? VirtualContent
    );
}
