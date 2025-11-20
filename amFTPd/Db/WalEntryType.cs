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
/// Represents the types of write-ahead log (WAL) entries used to track operations  performed on users, groups, and
/// sections within the system.
/// </summary>
/// <remarks>This enumeration categorizes operations into distinct groups based on their target entity: <list
/// type="bullet"> <item> <description><b>USER operations</b>: Includes adding, updating, and deleting
/// users.</description> </item> <item> <description><b>GROUP operations</b>: Includes adding, updating, and deleting
/// groups.</description> </item> <item> <description><b>SECTION operations</b>: Reserved for future use, includes
/// adding, updating, and deleting sections.</description> </item> </list> Each value is represented as a <see
/// cref="byte"/> to optimize storage in the WAL.</remarks>
public enum WalEntryType : byte
{
    // USER operations
    AddUser = 0,
    UpdateUser = 1,
    DeleteUser = 2,

    // GROUP operations
    AddGroup = 10,
    UpdateGroup = 11,
    DeleteGroup = 12,

    // SECTION operations (reserved for next module)
    AddSection = 20,
    UpdateSection = 21,
    DeleteSection = 22
}