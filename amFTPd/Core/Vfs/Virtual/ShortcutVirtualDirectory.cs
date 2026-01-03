using amFTPd.Core.Sections;

namespace amFTPd.Core.Vfs.Virtual;

/// <summary>
/// Virtual shortcut directories such as /TODAY, /0DAY, /MP3.
/// These redirect to section virtual roots without touching filesystem.
/// </summary>
public sealed class ShortcutVirtualDirectory
{
    private readonly SectionResolver _sections;

    public ShortcutVirtualDirectory(SectionResolver sections) => _sections = sections;

    public VfsResolveResult Resolve(string virtualPath)
    {
        virtualPath = virtualPath.TrimEnd('/');

        if (virtualPath.Length < 2)
            return VfsResolveResult.NotFound();

        var name = virtualPath.TrimStart('/');

        // resolve by section virtual root name
        var section = _sections.Resolve("/" + name);
        if (section is null)
            return VfsResolveResult.NotFound();

        return VfsResolveResult.Ok(
            new VfsNode(
                VfsNodeType.VirtualDirectory,
                section.VirtualRoot,
                null,
                null,
                null
            ));
    }
}