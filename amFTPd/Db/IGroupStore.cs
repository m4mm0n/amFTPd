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