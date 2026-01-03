namespace amFTPd.Core.Scene;

/// <summary>
/// Provides utility methods for classifying scene-related files based on their file names or extensions.
/// </summary>
/// <remarks>This class includes methods to identify common scene file types such as archives, SFV, NFO, and DIZ
/// files. All methods are static and the class cannot be instantiated.</remarks>
public static class SceneFileClassifier
{
    private static readonly HashSet<string> BaseArchiveExts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".rar",
            ".zip",
            ".7z",
            ".ace",
            ".arj",
            ".lha",
            ".lzh",
            ".cab"
        };

    public static bool IsArchive(string fileName)
    {
        var ext = Path.GetExtension(fileName);

        if (BaseArchiveExts.Contains(ext))
            return true;

        // .r00 .r01 .r99
        if (ext.Length == 4 &&
            ext[1] == 'r' &&
            char.IsDigit(ext[2]) &&
            char.IsDigit(ext[3]))
            return true;

        // .001 .002 .999
        if (ext.Length == 4 &&
            char.IsDigit(ext[1]) &&
            char.IsDigit(ext[2]) &&
            char.IsDigit(ext[3]))
            return true;

        return false;
    }

    public static bool IsSfv(string fileName)
        => fileName.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase);

    public static bool IsNfo(string fileName)
        => fileName.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase);

    public static bool IsDiz(string fileName)
        => fileName.EndsWith(".diz", StringComparison.OrdinalIgnoreCase);
}