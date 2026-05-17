/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           PluginLoadContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Isolated AssemblyLoadContext for a single plugin DLL.
 *      Each plugin loads into its own context so that:
 *        • its NuGet dependencies don't clash with the host or other plugins,
 *        • it can be unloaded and replaced on REHASH without a process restart.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using System.Reflection;
using System.Runtime.Loader;

namespace amFTPd.Core.Plugins;

/// <summary>
/// Isolated <see cref="AssemblyLoadContext"/> for one plugin DLL.
/// Uses <see cref="AssemblyDependencyResolver"/> to load the plugin's own
/// dependency graph, while still sharing the amFTPd.Plugin.Abstractions
/// contract assembly (and the BCL) with the host process.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// Creates a new load context for the plugin at <paramref name="pluginDllPath"/>.
    /// </summary>
    /// <param name="pluginDllPath">Absolute path to the plugin's main DLL.</param>
    public PluginLoadContext(string pluginDllPath)
        : base(
            name: Path.GetFileNameWithoutExtension(pluginDllPath),
            isCollectible: true)  // supports GC-based unload after all references drop
    {
        _resolver = new AssemblyDependencyResolver(pluginDllPath);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Delegate amFTPd.Plugin.Abstractions to the host context so the types
        // are identical (same AssemblyLoadContext → same Type objects → interface
        // implementations are recognised correctly).
        if (assemblyName.Name == "amFTPd.Plugin.Abstractions")
            return null; // null = use default (host) context

        // Let the resolver find plugin-local dependencies from the .deps.json
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is not null)
            return LoadFromAssemblyPath(path);

        // Fall back to the host context for anything the resolver doesn't know
        return null;
    }

    /// <inheritdoc />
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
