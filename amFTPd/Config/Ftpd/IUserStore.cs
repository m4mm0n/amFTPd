/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IUserStore.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x9C01067C
 *  
 *  Description:
 *      Defines the contract for a user store that manages user authentication and persistence operations for FTP users.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

namespace amFTPd.Config.Ftpd;

/// <summary>
/// Defines the contract for a user store that manages user authentication and persistence operations for FTP users.
/// </summary>
/// <remarks>Implementations of this interface are responsible for loading, saving, and managing user accounts,
/// including authentication and user lifecycle operations. Thread safety and persistence mechanisms depend on the
/// specific implementation.</remarks>
public interface IUserStore
{
    /// <summary>
    /// Attempts to authenticate a user with the specified username and password.
    /// </summary>
    /// <param name="user">The username to authenticate.</param>
    /// <param name="password">The password associated with the username.</param>
    /// <param name="account">When this method returns, contains the authenticated <see cref="FtpUser"/> object if authentication succeeds;
    /// otherwise, <see langword="null"/>. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the authentication is successful; otherwise, <see langword="false"/>.</returns>
    bool TryAuthenticate(string user, string password, out FtpUser? account);
    /// <summary>
    /// Handles the logout process for the specified FTP user.
    /// </summary>
    /// <remarks>This method is typically called when an FTP user session ends. Ensure that any necessary
    /// cleanup or resource release associated with the user is performed within this method.</remarks>
    /// <param name="user">The FTP user who is logging out. Cannot be <see langword="null"/>.</param>
    void OnLogout(FtpUser user);
    /// <summary>
    /// Finds and returns the user associated with the specified username.
    /// </summary>
    /// <param name="userName">The username of the user to find. Cannot be null or empty.</param>
    /// <returns>An <see cref="FtpUser"/> object representing the user if found; otherwise, <see langword="null"/>.</returns>
    FtpUser? FindUser(string userName);
    /// <summary>
    /// Retrieves all users from the FTP server.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="FtpUser"/> objects representing all users on the server. The
    /// collection will be empty if no users are found.</returns>
    IEnumerable<FtpUser> GetAllUsers();
    /// <summary>
    /// Attempts to add a new FTP user to the system.
    /// </summary>
    /// <remarks>This method does not throw exceptions for validation or operational errors. Instead, it
    /// returns <see langword="false"/> and provides an error message in the <paramref name="error"/>
    /// parameter.</remarks>
    /// <param name="user">The <see cref="FtpUser"/> object representing the user to be added. Cannot be <see langword="null"/>.</param>
    /// <param name="error">When this method returns, contains an error message describing why the user could not be added, or <see
    /// langword="null"/> if the operation was successful.</param>
    /// <returns><see langword="true"/> if the user was successfully added; otherwise, <see langword="false"/>.</returns>
    bool TryAddUser(FtpUser user, out string? error);
    /// <summary>
    /// Attempts to update the specified FTP user's information.
    /// </summary>
    /// <remarks>This method does not throw exceptions for validation or update failures. Instead, it returns
    /// <see langword="false"/> and provides an error message in the <paramref name="error"/> parameter.</remarks>
    /// <param name="user">The <see cref="FtpUser"/> object containing the updated user information. Cannot be <c>null</c>.</param>
    /// <param name="error">When this method returns, contains an error message describing the failure, if the update was unsuccessful;
    /// otherwise, <c>null</c>.</param>
    /// <returns><see langword="true"/> if the user information was successfully updated; otherwise, <see langword="false"/>.</returns>
    bool TryUpdateUser(FtpUser user, out string? error);
}