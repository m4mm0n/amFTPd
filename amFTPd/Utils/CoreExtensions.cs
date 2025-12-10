/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           CoreExtensions.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-01 05:44:45
 *  Last Modified:  2025-12-10 03:58:32
 *  CRC32:          0xDFD78BFC
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */




using System.Collections.Immutable;

namespace amFTPd.Utils
{
    internal static class CoreExtensions
    {
        public static bool HasFlag(this ImmutableHashSet<char> flags, char flag)
            => flags.Contains(char.ToUpperInvariant(flag));
    }
}
