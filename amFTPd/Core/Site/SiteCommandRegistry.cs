/*
* ====================================================================================================
*  Project:        amFTPd - a managed FTP daemon
*  Author:         Geir Gustavsen, ZeroLinez Softworx
*  Created:        2025-11-25
*  Last Modified:  2025-11-25
*  
*  License:
*      MIT License
*      https://opensource.org/licenses/MIT
*
*  Notes:
*      Simple in-memory implementation of ISectionStore. This is used when the
*      binary DB backend is not active, or as a lightweight wrapper over the
*      configuration-based SectionManager.
* ====================================================================================================
*/

namespace amFTPd.Core.Site;

/// <summary>
/// Builds and caches all SITE commands via reflection.
/// </summary>
public static class SiteCommandRegistry
{
    public static IReadOnlyDictionary<string, SiteCommandBase> Build(SiteCommandContext context)
    {
        var dict = new Dictionary<string, SiteCommandBase>(StringComparer.OrdinalIgnoreCase);

        // Look for all non-abstract types deriving from SiteCommandBase in our assembly
        var baseType = typeof(SiteCommandBase);
        var asm = baseType.Assembly;

        foreach (var type in asm.GetTypes())
        {
            if (type.IsAbstract || !baseType.IsAssignableFrom(type))
                continue;

            // Require parameterless constructor
            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            if (Activator.CreateInstance(type) is not SiteCommandBase cmd)
                continue;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Added: {cmd.Name.ToUpperInvariant()} ({cmd})");
#endif
            dict[cmd.Name.ToUpperInvariant()] = cmd;
        }

#if DEBUG
        Console.WriteLine($"A total of {dict.Count} commands ready!");
#endif

        return dict;
    }
}