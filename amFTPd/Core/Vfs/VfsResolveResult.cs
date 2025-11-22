namespace amFTPd.Core.Vfs;

/// <summary>
/// Represents the result of a Virtual File System (VFS) resolution operation, indicating whether the operation was
/// successful and providing additional details if necessary.
/// </summary>
/// <remarks>This type encapsulates the outcome of resolving a VFS node, including whether the resolution
/// succeeded, an optional error message in case of failure, and the resolved node if applicable. Use the provided
/// factory methods <see cref="NotFound(string?)"/>, <see cref="Denied(string?)"/>, and <see cref="Ok(VfsNode)"/> to
/// create instances representing common resolution outcomes.</remarks>
/// <param name="Success">A value indicating whether the resolution operation was successful. <see langword="true"/> if the operation
/// succeeded; otherwise, <see langword="false"/>.</param>
/// <param name="ErrorMessage">An optional error message describing the reason for failure, if <paramref name="Success"/> is <see
/// langword="false"/>. This value is <see langword="null"/> if the operation succeeded.</param>
/// <param name="Node">The resolved <see cref="VfsNode"/> if the operation was successful; otherwise, <see langword="null"/>.</param>
public sealed record VfsResolveResult(
    bool Success,
    string? ErrorMessage,
    VfsNode? Node
)
{
    public static VfsResolveResult NotFound(string? message = null)
        => new(false, message ?? "Not found.", null);

    public static VfsResolveResult Denied(string? message = null)
        => new(false, message ?? "Permission denied.", null);

    public static VfsResolveResult Ok(VfsNode node)
        => new(true, null, node);
}