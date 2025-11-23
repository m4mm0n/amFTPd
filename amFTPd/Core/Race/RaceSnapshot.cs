/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23
 *  Last Modified:  2025-11-23
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

namespace amFTPd.Core.Race
{
    /// <summary>
    /// Immutable snapshot of race (upload) activity for a release directory.
    /// </summary>
    /// <param name="ReleasePath">Virtual path to the release directory (e.g. "/0DAY/Release.Name-2025").</param>
    /// <param name="SectionName">Section this release belongs to (e.g. "0DAY").</param>
    /// <param name="StartedAt">When the first file was uploaded.</param>
    /// <param name="LastUpdatedAt">When the last file was uploaded.</param>
    /// <param name="UserBytes">Per-user total bytes uploaded.</param>
    /// <param name="TotalBytes">Total bytes uploaded for this release.</param>
    /// <param name="FileCount">Number of files uploaded.</param>
    public sealed record RaceSnapshot(
        string ReleasePath,
        string SectionName,
        DateTimeOffset StartedAt,
        DateTimeOffset LastUpdatedAt,
        IReadOnlyDictionary<string, long> UserBytes,
        long TotalBytes,
        int FileCount
    );
}
