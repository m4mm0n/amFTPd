/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IGroupStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 20:08:55
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xD0160B41
 *  
 *  Description:
 *      Defines the contract for managing FTP groups, including operations to retrieve, add, update, and delete groups.
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
/// Defines the contract for managing FTP groups, including operations to retrieve, add, update, and delete groups.
/// </summary>
/// <remarks>This interface provides methods for interacting with FTP groups, allowing for retrieval of
/// group information, as well as adding, updating, and deleting groups. Implementations of this interface should
/// ensure thread safety if used in a concurrent environment.</remarks>
public interface IGroupStore
{
    FtpGroup? FindGroup(string groupName);
    IEnumerable<FtpGroup> GetAllGroups();
    bool TryAddGroup(FtpGroup group, out string? error);
    bool TryUpdateGroup(FtpGroup group, out string? error);
    bool TryDeleteGroup(string groupName, out string? error);
    bool TryRenameGroup(string oldName, string newName, out string? error);
}