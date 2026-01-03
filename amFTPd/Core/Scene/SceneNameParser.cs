namespace amFTPd.Core.Scene;

/// <summary>
/// Provides utility methods for parsing and extracting information from scene release names.
/// </summary>
/// <remarks>This class is intended for use with standard scene release naming conventions, where group names are
/// typically appended after a hyphen ('-'). All members are static and the class cannot be instantiated.</remarks>
public static class SceneNameParser
{
    public static string? ExtractGroup(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
            return null;

        var idx = releaseName.LastIndexOf('-');
        if (idx <= 0 || idx == releaseName.Length - 1)
            return null;

        return releaseName[(idx + 1)..];
    }
}