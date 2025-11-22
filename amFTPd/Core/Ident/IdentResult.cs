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

namespace amFTPd.Core.Ident
{
    /// <summary>
    /// Represents the result of an identification operation, including its success status, associated user information,
    /// and metadata about the operation.
    /// </summary>
    /// <remarks>This record is immutable and provides a snapshot of the identification operation's outcome.
    /// Use the <see cref="Failed"/> property to represent a standardized failure result.</remarks>
    /// <param name="Success">Indicates whether the identification operation was successful. <see langword="true"/> if the operation
    /// succeeded; otherwise, <see langword="false"/>.</param>
    /// <param name="Username">The username associated with the identification result, or <see langword="null"/> if unavailable.</param>
    /// <param name="OpsSystem">The operating system or platform associated with the identification result, or <see langword="null"/> if
    /// unavailable.</param>
    /// <param name="RawResponse">The raw response string from the identification operation, or <see langword="null"/> if unavailable.</param>
    /// <param name="TimestampUtc">The timestamp, in UTC, when the identification operation was performed.</param>
    public sealed record IdentResult(
        bool Success,
        string? Username,
        string? OpsSystem,
        string? RawResponse,
        DateTimeOffset TimestampUtc
    )
    {
        public static readonly IdentResult Failed =
            new(false, null, null, null, DateTimeOffset.UtcNow);
    }
}
