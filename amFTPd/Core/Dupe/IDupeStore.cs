/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-01
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

namespace amFTPd.Core.Dupe;

public interface IDupeStore
{
    /// <summary>Find a dupe by exact section + release name. Returns null if not found.</summary>
    DupeEntry? Find(string sectionName, string releaseName);

    /// <summary>
    /// Search dupes using a simple wildcard pattern (* and ?) on ReleaseName.
    /// Optional section filter and result limit.
    /// </summary>
    IReadOnlyList<DupeEntry> Search(string pattern, string? sectionName = null, int limit = 50);

    /// <summary>
    /// Insert or update a dupe entry. Matching is done by (SectionName, ReleaseName).
    /// </summary>
    void Upsert(DupeEntry entry);

    /// <summary>
    /// Remove a dupe entry (eg. on WIPE).
    /// </summary>
    bool Remove(string sectionName, string releaseName);
}