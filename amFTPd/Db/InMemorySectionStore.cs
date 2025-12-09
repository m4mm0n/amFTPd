/*
* ====================================================================================================
*  Project:        amFTPd - a managed FTP daemon
*  Author:         Geir Gustavsen, ZeroLinez Softworx
*  Created:        2025-11-25
*  Last Modified:  2025-11-28
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

using amFTPd.Config.Ftpd;
//using DbSection = amFTPd.Db.FtpSection;

namespace amFTPd.Db
{
    /// <summary>
    /// In-memory implementation of <see cref="ISectionStore"/>.
    /// </summary>
    /// <remarks>
    /// This store can be constructed either from a <see cref="SectionManager"/> (config-based
    /// sections) or directly from an enumerable of DB-level <see cref="DbSection"/> records.
    /// It is primarily intended for non-DB mode or testing, where a lightweight section
    /// store is sufficient.
    /// </remarks>
    internal sealed class InMemorySectionStore : ISectionStore
    {
        private readonly Dictionary<string, Config.Ftpd.FtpSection> _sections =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemorySectionStore"/> class
        /// from an enumerable of DB-level sections.
        /// </summary>
        public InMemorySectionStore(IEnumerable<Config.Ftpd.FtpSection> sections)
        {
            if (sections is null) throw new ArgumentNullException(nameof(sections));

            _sections = sections
                .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemorySectionStore"/> class
        /// from a configuration-based <see cref="SectionManager"/>.
        /// </summary>
        public InMemorySectionStore(SectionManager manager)
        {
            if (manager is null)
                throw new ArgumentNullException(nameof(manager));

            var dict = new Dictionary<string, Config.Ftpd.FtpSection>(StringComparer.OrdinalIgnoreCase);

            foreach (var cfg in manager.GetSections())
                dict[cfg.Name] = cfg;   // 1:1 now

            _sections = dict;
        }

        /// <inheritdoc />
        public Config.Ftpd.FtpSection? FindSection(string sectionName) =>
            sectionName is null ? throw new ArgumentNullException(nameof(sectionName)) :
            _sections.TryGetValue(sectionName, out var s) ? s : null;

        /// <inheritdoc />
        public IEnumerable<Config.Ftpd.FtpSection> GetAllSections() => _sections.Values;

        /// <inheritdoc />
        public bool TryUpdateSection(Config.Ftpd.FtpSection section, out string? error)
        {
            if (section is null)
            {
                error = "Section cannot be null.";
                return false;
            }

            if (!_sections.ContainsKey(section.Name))
            {
                error = $"Section '{section.Name}' does not exist.";
                return false;
            }

            _sections[section.Name] = section;
            error = null;
            return true;
        }

        /// <inheritdoc />
        public bool TryDeleteSection(string sectionName, out string? error)
        {
            if (sectionName is null)
            {
                error = "Section name cannot be null.";
                return false;
            }

            if (!_sections.Remove(sectionName))
            {
                error = $"Section '{sectionName}' does not exist.";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryAddSection(Config.Ftpd.FtpSection section, out string? error)
        {
            if (_sections.ContainsKey(section.Name))
            {
                error = "Section exists.";
                return false;
            }

            _sections[section.Name] = section;
            error = null;
            return true;
        }
    }
}
