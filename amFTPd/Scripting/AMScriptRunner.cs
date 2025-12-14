/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AMScriptRunner.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 19:01:19
 *  Last Modified:  2025-12-14 19:05:02
 *  CRC32:          0x8755422F
 *  
 *  Description:
 *      Thin adapter that exposes an <see cref="AMScriptEngine"/> as an <see cref="IAmScript"/>.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Scripting
{
    /// <summary>
    /// Thin adapter that exposes an <see cref="AMScriptEngine"/> as an <see cref="IAmScript"/>.
    /// </summary>
    public sealed class AMScriptRunner : IAmScript
    {
        private readonly AMScriptEngine _engine;

        /// <summary>
        /// Gets the name associated with the current instance.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Gets the file system path to the script associated with this instance.
        /// </summary>
        public string ScriptPath { get; }
        /// <summary>
        /// Initializes a new instance of the <see cref="AMScriptRunner"/> class with the specified name, script engine,
        /// and script path.
        /// </summary>
        /// <param name="name">The unique name used to identify this script runner instance. Cannot be null or empty.</param>
        /// <param name="engine">The <see cref="AMScriptEngine"/> instance used to execute scripts. Cannot be null.</param>
        /// <param name="scriptPath">The file system path to the script to be executed. Cannot be null or empty.</param>
        public AMScriptRunner(string name, AMScriptEngine engine, string scriptPath)
        {
            Name = name;
            _engine = engine;
            ScriptPath = scriptPath;
        }
        /// <summary>
        /// Evaluates the specified AMScript context and returns the result of the script execution.
        /// </summary>
        /// <param name="ctx">The AMScript context containing variables, input data, and execution settings for the script. Cannot be
        /// <c>null</c>.</param>
        /// <returns>An <see cref="AMScriptResult"/> representing the outcome of the script evaluation, including any output or
        /// errors generated during execution.</returns>
        public AMScriptResult Evaluate(AMScriptContext ctx)
            => _engine.EvaluateUpload(ctx); // or EvaluateDownload; your call per use-site
    }
}
