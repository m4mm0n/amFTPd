/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02
 *  Last Modified:  2025-12-02
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

namespace amFTPd.Core.Zipscript;

/// <summary>
/// Represents the status of a release in a zipscript system, including its path, section, and associated files.
/// </summary>
/// <remarks>This class provides information about a release, such as its virtual directory path, section
/// name, whether it includes an SFV file, and whether the release is marked as complete. It also contains a
/// collection of files associated with the release.</remarks>
public sealed class ZipscriptReleaseStatus
{
    public string ReleasePath { get; init; } = string.Empty;  // virtual path to directory
    public string SectionName { get; init; } = string.Empty;
    public bool HasSfv { get; init; }
    public bool IsComplete { get; init; }

    public IReadOnlyList<ZipscriptFileInfo> Files { get; init; } = Array.Empty<ZipscriptFileInfo>();
}