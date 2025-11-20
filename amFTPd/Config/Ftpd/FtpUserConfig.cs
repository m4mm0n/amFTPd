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

namespace amFTPd.Config.Ftpd;

/// <summary>
/// Represents the configuration for FTP users, containing a collection of user-specific settings.
/// </summary>
/// <param name="Users">A list of user configurations, each defining individual user settings and permissions.</param>
public sealed record FtpUserConfig(
    List<FtpUserConfigUser> Users
)
{
    /// <summary>
    /// Gets an empty instance of <see cref="FtpUserConfig"/> with no user configurations.
    /// </summary>
    /// <remarks>
    /// This property provides a default, immutable instance of <see cref="FtpUserConfig"/> 
    /// with an empty list of user configurations. It can be used as a placeholder or 
    /// default value when no specific configuration is required.
    /// </remarks>
    public static FtpUserConfig Empty => new(new List<FtpUserConfigUser>());
}