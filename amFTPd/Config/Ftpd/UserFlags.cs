/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           UserFlags.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-13 04:32:32
 *  CRC32:          0x8964B278
 *  
 *  Description:
 *      Defines all supported built-in user flags. Flags are stored as raw characters in FlagsRaw.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Defines all supported built-in user flags.
    /// Flags are stored as raw characters in FlagsRaw.
    /// </summary>
    public static class UserFlags
    {
        /// <summary>
        /// Provides a set of characters representing all recognized user flags within the system.
        /// </summary>
        /// <remarks>Each character in the set corresponds to a specific user permission or status, such
        /// as master administrator, site operator, or banned user. The set can be used to validate or enumerate
        /// supported flags when processing user accounts or permissions.</remarks>
        public static readonly HashSet<char> KnownFlags =
        [
            'M', // Master Admin
            'S', // SiteOp
            '1', // No Ratio
            'H', // Hidden
            'R', // Require TLS
            'Z', // Kick immune
            'V', // VIP boost
            'B', // Banned
            'A', // Auto-delete allowed
            'D', // Disable downloads
            'U' // Disable uploads
        ];

        public static bool IsKnown(char flag)
            => KnownFlags.Contains(char.ToUpperInvariant(flag));
    }
}
