/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
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

namespace amFTPd.Db;

/// <summary>
/// Defines a contract for managing and retrieving FTP configuration sections.
/// </summary>
/// <remarks>This interface provides methods to find, retrieve, add, update, and delete FTP sections.
/// Implementations of this interface are expected to handle the storage and management of <see cref="FtpSection"/>
/// objects, ensuring thread safety and data consistency where applicable.</remarks>
public interface ISectionStore
{
    FtpSection? FindSection(string sectionName);
    IEnumerable<FtpSection> GetAllSections();

    bool TryAddSection(FtpSection section, out string? error);
    bool TryUpdateSection(FtpSection section, out string? error);
    bool TryDeleteSection(string sectionName, out string? error);
}