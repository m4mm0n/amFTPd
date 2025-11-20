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

namespace amFTPd.Db
{
    /// <summary>
    /// Represents a group in an FTP system, including its name, description, associated users, and section-specific
    /// credits.
    /// </summary>
    /// <param name="GroupName">The name of the FTP group. This value cannot be null or empty.</param>
    /// <param name="Description">A brief description of the FTP group. This value can be null or empty.</param>
    /// <param name="Users">A list of usernames associated with the group. This list cannot be null but may be empty.</param>
    /// <param name="SectionCredits">A dictionary mapping section names to their respective credit values for the group. The dictionary cannot be
    /// null but may be empty. Keys represent section names, and values represent the credits.</param>
    public sealed record FtpGroup(
        string GroupName,
        string Description,
        List<string> Users,
        Dictionary<string, long> SectionCredits
    );
}
