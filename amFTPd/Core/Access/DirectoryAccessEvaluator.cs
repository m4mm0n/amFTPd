/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           DirectoryAccessEvaluator.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-13 04:18:09
 *  CRC32:          0x7E5CC174
 *  
 *  Description:
 *      Evaluates directory access flags based on configured <see cref="DirectoryRule"/> entries. Uses longest-prefix match o...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Config.Ftpd.RatioRules;

namespace amFTPd.Core.Access;

/// <summary>
/// Evaluates directory access flags based on configured <see cref="DirectoryRule"/> entries.
/// Uses longest-prefix match on normalized virtual paths.
/// </summary>
public sealed class DirectoryAccessEvaluator
{
    private readonly IReadOnlyDictionary<string, DirectoryRule> _rules;

    /// <summary>
    /// Initializes a new instance of the DirectoryAccessEvaluator class using the specified set of directory access
    /// rules.
    /// </summary>
    /// <param name="rules">A read-only dictionary containing directory access rules, keyed by directory path. Each rule defines the
    /// access permissions for its associated directory.</param>
    /// <exception cref="ArgumentNullException">Thrown if the rules parameter is null.</exception>
    public DirectoryAccessEvaluator(IReadOnlyDictionary<string, DirectoryRule> rules) => _rules = rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>
    /// Evaluates the effective access flags for the given virtual path.
    /// </summary>
    /// <param name="virtualPath">A virtual path such as "/0DAY/dir/file.rar".</param>
    public DirectoryAccess Evaluate(string? virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            virtualPath = "/";

        virtualPath = Normalize(virtualPath);

        DirectoryRule? bestRule = null;
        var bestKeyLength = -1;

        foreach (var kv in _rules)
        {
            var rawKey = kv.Key;
            if (string.IsNullOrWhiteSpace(rawKey))
                continue;

            var key = Normalize(rawKey);

            if (!virtualPath.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                continue;

            // Longest prefix wins
            if (key.Length > bestKeyLength)
            {
                bestKeyLength = key.Length;
                bestRule = kv.Value;
            }
        }

        var effective = bestRule ?? DirectoryRule.Empty;

        var canList = effective.AllowList;
        var canUpload = effective.AllowUpload;
        var canDownload = effective.AllowDownload;

        return new DirectoryAccess(
            CanList: canList,
            CanUpload: canUpload,
            CanDownload: canDownload
        );
    }

    private static string Normalize(string path)
    {
        path = path.Replace('\\', '/');
        if (!path.StartsWith("/", StringComparison.Ordinal))
            path = "/" + path;
        // no trailing slash trimming – keeps prefix semantics simple
        return path;
    }
}