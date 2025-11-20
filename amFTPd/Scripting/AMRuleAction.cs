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
/// Specifies the possible actions that can be taken when evaluating a rule.
/// </summary>
/// <remarks>This enumeration is typically used to define the outcome of a rule evaluation, such as
/// allowing or denying a specific operation.</remarks>
public enum AMRuleAction
{
    None,
    Allow,
    Deny
}