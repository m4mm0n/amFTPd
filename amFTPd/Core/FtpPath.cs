/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpPath.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xBF172B04
 *  
 *  Description:
 *      Provides utility methods for normalizing and resolving FTP paths to a consistent POSIX-style format.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core;

/// <summary>
/// Provides utility methods for normalizing and resolving FTP paths to a consistent POSIX-style format.
/// </summary>
/// <remarks>This class is designed to handle path normalization within a virtual root, ensuring that paths are
/// resolved to their canonical form. It supports collapsing redundant path segments such as "." and "..", and converts
/// backslashes to forward slashes for consistency.</remarks>
internal static class FtpPath
{
    /// <summary>
    /// Normalizes a given input path by replacing backslashes with forward slashes and resolving relative paths.
    /// </summary>
    /// <param name="current">The current base path to resolve relative paths against. This should be a valid path.</param>
    /// <param name="input">The input path to normalize. If null, empty, or whitespace, the <paramref name="current"/> path is returned
    /// unchanged.</param>
    /// <returns>A normalized path with forward slashes and resolved relative segments. If <paramref name="input"/> is null,
    /// empty, or whitespace, the <paramref name="current"/> path is returned.</returns>
    public static string Normalize(string current, string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return current;
        var p = input.Replace('\\', '/');
        return Collapse(p.StartsWith('/') ? p : $"{current.TrimEnd('/')}/{p}");
    }

    private static string Collapse(string p)
    {
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new Stack<string>();
        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.Pop();
                continue;
            }
            stack.Push(part);
        }
        var arr = stack.Reverse().ToArray();
        return "/" + string.Join('/', arr);
    }
}