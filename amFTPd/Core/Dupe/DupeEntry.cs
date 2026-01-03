/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           DupeEntry.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 04:34:39
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x33C781C6
 *  
 *  Description:
 *      A single dupe entry for a release. This is intentionally simple and generic so it can outlive internal refactors.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

namespace amFTPd.Core.Dupe
{
    /// <summary>
    /// A single dupe entry for a release.
    /// This is intentionally simple and generic so it can outlive internal refactors.
    /// </summary>
    public sealed record DupeEntry
    {
        /// <summary>Canonical release name, e.g. "Artist-Album-2025-GRP". Case-insensitive.</summary>
        public string ReleaseName { get; init; } = string.Empty;

        /// <summary>Section name, e.g. "MP3", "XVID".</summary>
        public string SectionName { get; init; } = string.Empty;

        /// <summary>Virtual path to the release directory, e.g. "/MP3/Artist-Album-2025-GRP".</summary>
        public string VirtualPath { get; init; } = string.Empty;

        /// <summary>Total size in bytes of the release (best effort).</summary>
        public long TotalBytes { get; init; }

        /// <summary>When the release was first seen by amFTPd.</summary>
        public DateTimeOffset FirstSeen { get; init; }

        /// <summary>Last time we updated the entry (e.g. new files / nukes).</summary>
        public DateTimeOffset LastUpdated { get; init; }

        /// <summary>Uploader nick (or last uploader) if known.</summary>
        public string? UploaderUser { get; init; }

        /// <summary>Uploader group if known.</summary>
        public string? UploaderGroup { get; init; }

        /// <summary>True if this release has been nuked.</summary>
        public bool IsNuked { get; init; }

        /// <summary>Nuke reason text.</summary>
        public string? NukeReason { get; init; }

        /// <summary>Nuke multiplier (eg. 3x, 5x).</summary>
        public int NukeMultiplier { get; init; }

        public string Key => MakeKey(SectionName, ReleaseName);

        public static string MakeKey(string sectionName, string releaseName) => $"{sectionName.Trim().ToUpperInvariant()}|{releaseName.Trim().ToUpperInvariant()}";

        public static DupeEntry FromRelease(DupeRelease r)
        {
            if (r is null) throw new ArgumentNullException(nameof(r));

            return new DupeEntry
            {
                SectionName = r.Section,
                ReleaseName = r.ReleaseName,

                // DupeStore still keeps VirtualPath for now
                VirtualPath = $"/{r.Section}/{r.ReleaseName}",

                TotalBytes = r.TotalBytes,
                FirstSeen = r.FirstSeen,
                LastUpdated = r.LastUpdated,

                UploaderGroup = r.Group,
                UploaderUser = null, // not known here

                IsNuked = r.IsNuked,
                NukeReason = r.NukeReason,
                NukeMultiplier = r.NukeMultiplier > 0
                    ? (int)Math.Round(r.NukeMultiplier)
                    : 0
            };
        }
    }
}
