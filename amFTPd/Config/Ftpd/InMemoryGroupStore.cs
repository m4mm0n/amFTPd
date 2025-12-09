/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-24
 *  Last Modified:  2025-11-24
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

using amFTPd.Db;

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Provides an in-memory implementation of the <see cref="IGroupStore"/> interface for managing FTP groups.
    /// </summary>
    /// <remarks>This class maintains a collection of <see cref="FtpGroup"/> objects in memory, allowing
    /// operations such as adding, updating, deleting, and renaming groups. Group names are compared using a
    /// case-insensitive string comparison. This implementation is not thread-safe and is intended for scenarios where
    /// concurrent access is not required.</remarks>
    internal sealed class InMemoryGroupStore : IGroupStore
    {
        private readonly Dictionary<string, FtpGroup> _groups = new(StringComparer.OrdinalIgnoreCase);

        public FtpGroup? FindGroup(string groupName)
            => _groups.TryGetValue(groupName, out var g) ? g : null;

        public IEnumerable<FtpGroup> GetAllGroups() => _groups.Values;

        public bool TryAddGroup(FtpGroup group, out string? error)
        {
            if (_groups.ContainsKey(group.GroupName))
            {
                error = "Group already exists.";
                return false;
            }

            _groups[group.GroupName] = group;
            error = null;
            return true;
        }

        public bool TryUpdateGroup(FtpGroup group, out string? error)
        {
            if (!_groups.ContainsKey(group.GroupName))
            {
                error = "Group not found.";
                return false;
            }

            _groups[group.GroupName] = group;
            error = null;
            return true;
        }

        public bool TryDeleteGroup(string groupName, out string? error)
        {
            if (!_groups.Remove(groupName))
            {
                error = "Group not found.";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryRenameGroup(string oldName, string newName, out string? error)
        {
            if (!_groups.TryGetValue(oldName, out var g))
            {
                error = "Group not found.";
                return false;
            }

            if (_groups.ContainsKey(newName))
            {
                error = "New group name already exists.";
                return false;
            }

            _groups.Remove(oldName);
            _groups[newName] = g with { GroupName = newName };
            error = null;
            return true;
        }
    }

}
