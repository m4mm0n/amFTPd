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

namespace amFTPd.Core.Ident;

/// <summary>
/// Represents an exception that is thrown when an identity policy violation occurs.
/// </summary>
/// <remarks>This exception is typically used to indicate that an operation has failed due to a violation
/// of a predefined identity policy. The specific policy violation should be described in the exception
/// message.</remarks>
public sealed class IdentPolicyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdentPolicyException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public IdentPolicyException(string message) : base(message) { }
}