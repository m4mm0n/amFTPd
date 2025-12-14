/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ScriptHost.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 19:05:00
 *  Last Modified:  2025-12-14 19:05:00
 *  CRC32:          0x5CD3CB99
 *  
 *  Description:
 *      Central registry for AMScript instances and script-related settings. Currently minimal; ready for future "external tr...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using System.Collections.Concurrent;
using amFTPd.Config.Scripting;

namespace amFTPd.Scripting;

/// <summary>
/// Central registry for AMScript instances and script-related settings.
/// Currently minimal; ready for future "external triggers" and per-script limits.
/// </summary>
public sealed class ScriptHost
{
    private readonly ScriptConfig _config;
    private readonly ConcurrentDictionary<string, IAmScript> _scripts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptHost"/> class using the specified configuration.
    /// </summary>
    /// <param name="config">The configuration settings to use for the script host. Cannot be <c>null</c>.</param>
    public ScriptHost(ScriptConfig config) => _config = config;
    /// <summary>
    /// Gets the current script configuration settings.
    /// </summary>
    public ScriptConfig Config => _config;
    /// <summary>
    /// Registers a script with the current collection, associating it with its unique name.
    /// </summary>
    /// <remarks>If a script with the same name already exists in the collection, it will be replaced
    /// by the new script.</remarks>
    /// <param name="script">The script to register. The <see cref="IAmScript.Name"/> property must provide a unique identifier for the
    /// script within the collection. Cannot be <see langword="null"/>.</param>
    public void Register(IAmScript script) => _scripts[script.Name] = script;
    /// <summary>
    /// Attempts to retrieve a script with the specified name.
    /// </summary>
    /// <param name="name">The name of the script to retrieve. Cannot be <see langword="null"/>.</param>
    /// <param name="script">When this method returns, contains the script associated with the specified name, if found; otherwise, <see
    /// langword="null"/>.</param>
    /// <returns><see langword="true"/> if a script with the specified name was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGet(string name, out IAmScript? script)
        => _scripts.TryGetValue(name, out script);
}