/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ISectionStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 20:10:38
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xFD416773
 *  
 *  Description:
 *      Defines a contract for managing and retrieving FTP configuration sections.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */




namespace amFTPd.Db;

/// <summary>
/// Defines a contract for managing and retrieving FTP configuration sections.
/// </summary>
/// <remarks>This interface provides methods to find, retrieve, add, update, and delete FTP sections.
/// Implementations of this interface are expected to handle the storage and management of <see cref="Config.Ftpd.FtpSection"/>
/// objects, ensuring thread safety and data consistency where applicable.</remarks>
public interface ISectionStore
{
    Config.Ftpd.FtpSection? FindSection(string sectionName);
    IEnumerable<Config.Ftpd.FtpSection> GetAllSections();

    bool TryAddSection(Config.Ftpd.FtpSection section, out string? error);
    bool TryUpdateSection(Config.Ftpd.FtpSection section, out string? error);
    bool TryDeleteSection(string sectionName, out string? error);
}