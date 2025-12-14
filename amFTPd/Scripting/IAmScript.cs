/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IAmScript.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 19:05:00
 *  Last Modified:  2025-12-14 19:05:00
 *  CRC32:          0x9C17B37D
 *  
 *  Description:
 *      Minimal interface for a script that can evaluate an <see cref="AMScriptContext"/>.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Scripting;

/// <summary>
/// Minimal interface for a script that can evaluate an <see cref="AMScriptContext"/>.
/// </summary>
public interface IAmScript
{
    /// <summary>
    /// The name of the script.
    /// </summary>
    string Name { get; }
    /// <summary>
    /// The path to the script file.
    /// </summary>
    string ScriptPath { get; }
    /// <summary>
    /// Evaluates the provided <see cref="AMScriptContext"/> and returns the result of the evaluation.
    /// </summary>
    /// <param name="ctx">The context of the script execution, containing relevant information such as user details, paths, and event data.</param>
    /// <returns>An <see cref="AMScriptResult"/> representing the outcome of the evaluation, including actions, limits, and optional messages.</returns>
    AMScriptResult Evaluate(AMScriptContext ctx);
}