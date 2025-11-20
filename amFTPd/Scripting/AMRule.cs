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

namespace amFTPd.Scripting;

/// <summary>
/// Represents a rule consisting of a condition and an associated action to be performed when the condition is met.
/// </summary>
/// <remarks>This class is immutable. Once an instance is created, the condition and action cannot be
/// modified.</remarks>
internal sealed class AMRule
{
    /// <summary>
    /// Gets the condition associated with the current context.
    /// </summary>
    public string Condition { get; }
    /// <summary>
    /// Gets the name of the action to be performed.
    /// </summary>
    public string Action { get; }
    /// <summary>
    /// Initializes a new instance of the <see cref="AMRule"/> class with the specified condition and action.
    /// </summary>
    /// <param name="condition">The condition that determines when the rule is applied. Cannot be null or empty.</param>
    /// <param name="action">The action to perform when the condition is met. Cannot be null or empty.</param>
    public AMRule(string condition, string action)
    {
        Condition = condition;
        Action = action;
    }
}