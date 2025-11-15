namespace amFTPd.Config.Ftpd;

/// <summary>
/// Defines the contract for managing and authenticating FTP users.
/// </summary>
/// <remarks>This interface provides methods for user authentication, retrieval, and management within an FTP
/// system.  Implementations of this interface are responsible for handling user-related operations such as adding, 
/// updating, and authenticating users, as well as managing user sessions.</remarks>
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