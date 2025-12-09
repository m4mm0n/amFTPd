/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03
 *  Last Modified:  2025-12-03
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

namespace amFTPd.Core.Events;

/// <summary>
/// Represents the types of events that can occur during FTP operations.
/// </summary>
/// <remarks>This enumeration defines various FTP-related events, such as file uploads, downloads,
/// directory operations,  user authentication events, and other specialized actions. It can be used to categorize
/// or handle specific  FTP events in an application.</remarks>
public enum FtpEventType
{
    Upload,
    Download,
    Delete,
    Mkdir,
    Rmdir,
    Login,
    Logout,
    Nuke,
    Wipe,
    Pre,
    RaceUpdate,
    RaceComplete,
    ZipscriptStatus
}