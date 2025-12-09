/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteCommandRegistry.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 00:28:50
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x9CE860CB
 *  
 *  Description:
 *      Builds and caches all SITE commands via reflection.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





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