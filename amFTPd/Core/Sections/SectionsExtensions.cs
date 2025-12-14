/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SectionsExtensions.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 17:52:46
 *  Last Modified:  2025-12-14 18:02:05
 *  CRC32:          0x7E0D0303
 *  
 *  Description:
 *      Provides extension methods for working with <see cref="SectionManager"/> and section name resolution.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Sections
{
    /// <summary>
    /// Provides extension methods for working with <see cref="SectionManager"/> and section name resolution.
    /// </summary>
    /// <remarks>The <see cref="SectionExtensions"/> class offers helper methods to simplify common operations
    /// such as finding sections by name or alias and normalizing section names. These methods extend the functionality
    /// of <see cref="SectionManager"/> to support flexible section lookups and name normalization scenarios.</remarks>
    public static class SectionExtensions
    {
        /// <summary>
        /// Finds a section by canonical name or any alias.
        /// </summary>
        public static FtpSection? FindByNameOrAlias(this SectionManager manager, string nameOrAlias)
        {
            if (manager is null) throw new ArgumentNullException(nameof(manager));
            if (string.IsNullOrWhiteSpace(nameOrAlias))
                return null;

            var upper = nameOrAlias.ToUpperInvariant();

            foreach (var sec in manager.GetSections())
            {
                if (sec.Name.Equals(upper, StringComparison.OrdinalIgnoreCase))
                    return sec;

                if (sec.Aliases is not null &&
                    sec.Aliases.Any(a => a.Equals(upper, StringComparison.OrdinalIgnoreCase)))
                    return sec;
            }

            return null;
        }

        /// <summary>
        /// Normalizes a section name to its canonical name if it matches an alias.
        /// </summary>
        public static string NormalizeSectionName(
            this SectionManager manager,
            string candidate)
        {
            var sec = manager.FindByNameOrAlias(candidate);
            return sec?.Name ?? candidate;
        }
    }
}
