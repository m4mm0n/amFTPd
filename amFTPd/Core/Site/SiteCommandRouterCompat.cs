/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteCommandRouterCompat.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 17:57:48
 *  Last Modified:  2025-12-14 17:58:00
 *  CRC32:          0xDDB378D0
 *  
 *  Description:
 *      Helpers for SITE command compatibility (aliases, banners, etc.).
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

namespace amFTPd.Core.Site
{
    /// <summary>
    /// Helpers for SITE command compatibility (aliases, banners, etc.).
    /// </summary>
    public static class SiteCommandRouterCompat
    {
        private static readonly Dictionary<string, string> BuiltInAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // A few conservative, high-value aliases.
                // You can expand this table from config later if you want.
                { "STAT", "STATS" },        // gl: SITE STAT  → amFTPd: SITE STATS
                { "PRE",  "PRE" },          // keep as-is, but here for clarity
                { "WHOAMI", "WHO" },        // if some scripts use WHOAMI
                { "HELP", "HELP" }          // explicit
            };

        /// <summary>
        /// Returns the canonical SITE verb, applying alias mapping when enabled.
        /// </summary>
        public static string NormalizeVerb(string verb, FtpCompatibilityConfig? compat)
        {
            if (string.IsNullOrWhiteSpace(verb))
                return string.Empty;

            var upper = verb.ToUpperInvariant();

            if (compat is null || !compat.EnableCommandAliases)
                return upper;

            if (BuiltInAliases.TryGetValue(upper, out var mapped))
                return mapped.ToUpperInvariant();

            return upper;
        }

        /// <summary>
        /// Returns a short compatibility description for banners and SITE VERS.
        /// </summary>
        public static string DescribeCompat(FtpCompatibilityConfig? compat)
        {
            if (compat is null)
                return "compat:none";

            return $"compat:{(string.IsNullOrWhiteSpace(compat.Profile) ? "custom" : compat.Profile)}";
        }
    }
}
