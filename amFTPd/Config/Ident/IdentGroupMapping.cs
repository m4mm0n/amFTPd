/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-22
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

namespace amFTPd.Config.Ident
{
    /// <summary>
    /// Represents a mapping between an identifier user and a group name.
    /// </summary>
    /// <param name="IdentUser">The identifier of the user associated with the group.</param>
    /// <param name="GroupName">The name of the group to which the user is mapped.</param>
    public sealed record IdentGroupMapping(
        string IdentUser,
        string GroupName
    );
}
