/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           UserGroupResolver.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-13 04:45:42
 *  CRC32:          0xF95D4582
 *  
 *  Description:
 *      Resolves user-group-related authorization decisions. glFTPd-style OR stacking: - If any group allows, permission is g...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Config.Ftpd;

namespace amFTPd.Security
{
    /// <summary>
    /// Resolves user-group-related authorization decisions.
    /// glFTPd-style OR stacking:
    ///  - If any group allows, permission is granted.
    /// </summary>
    public static class UserGroupResolver
    {
        /// <summary>
        /// Determines whether the specified user belongs to the given group, using a case-insensitive comparison.
        /// </summary>
        /// <remarks>Group name comparisons are performed using a case-insensitive ordinal comparison. If
        /// the group name is null or empty, the method always returns <see langword="false"/>.</remarks>
        /// <param name="user">The user whose group membership is to be checked. Cannot be null.</param>
        /// <param name="group">The name of the group to check for membership. If null or empty, the method returns <see langword="false"/>.</param>
        /// <returns>true if the user belongs to the specified group; otherwise, false.</returns>
        public static bool BelongsToGroup(FtpUser user, string group) =>
            !string.IsNullOrEmpty(group) && user.AllGroups.Any(g =>
                g != null && g.Equals(group, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// If ANY group grants a permission → allowed.
        /// </summary>
        public static bool ResolveStacked(IEnumerable<bool> groupPermissions)
            => groupPermissions.Any(p => p);
    }
}
