/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           InMemoryGroupStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-24 05:39:19
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x7932326A
 *  
 *  Description:
 *      Provides an in-memory implementation of the <see cref="IGroupStore"/> interface for managing FTP groups.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Linq;
using amFTPd.Db;

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Provides an in-memory implementation of the <see cref="IGroupStore"/> interface for managing FTP groups.
    /// </summary>
    /// <remarks>This class maintains a collection of <see cref="FtpGroup"/> objects in memory, allowing
    /// operations such as adding, updating, deleting, and renaming groups. Group names are compared using a
    /// case-insensitive string comparison. This implementation is thread-safe.</remarks>
    internal sealed class InMemoryGroupStore : IGroupStore
    {
        private readonly Dictionary<string, FtpGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        public FtpGroup? FindGroup(string groupName)
        {
            lock (_sync)
            {
                return _groups.TryGetValue(groupName, out var g) ? g : null;
            }
        }

        public IEnumerable<FtpGroup> GetAllGroups()
        {
            lock (_sync)
            {
                return _groups.Values.ToList();
            }
        }

        public bool TryAddGroup(FtpGroup group, out string? error)
        {
            lock (_sync)
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
        }

        public bool TryUpdateGroup(FtpGroup group, out string? error)
        {
            lock (_sync)
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
        }

        public bool TryDeleteGroup(string groupName, out string? error)
        {
            lock (_sync)
            {
                if (!_groups.Remove(groupName))
                {
                    error = "Group not found.";
                    return false;
                }

                error = null;
                return true;
            }
        }

        public bool TryRenameGroup(string oldName, string newName, out string? error)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = "New group name cannot be empty.";
                return false;
            }

            lock (_sync)
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

}
