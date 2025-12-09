/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           DirectoryRule.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xCAFA4DC6
 *  
 *  Description:
 *      Maps a path prefix to a logical ratio section with additional flags.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System;

namespace amFTPd.Config.Ftpd.RatioRules
{
    /// <summary>
    /// Maps a path prefix to a logical ratio section with additional flags.
    /// </summary>
    public sealed record DirectoryRule
    {
        public static DirectoryRule Empty { get; } = new();

        /// <summary>Section name this directory belongs to.</summary>
        public string SectionName { get; init; } = string.Empty;

        /// <summary>Virtual path prefix (e.g. "/0DAY").</summary>
        public string PathPrefix { get; init; } = "/";

        /// <summary>Alias for <see cref="PathPrefix"/> for older code.</summary>
        public string VirtualPath
        {
            get => PathPrefix;
            init => PathPrefix = value;
        }

        /// <summary>Whether this rule is enabled.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>Allow uploads here.</summary>
        public bool AllowUpload { get; init; } = true;

        /// <summary>Allow downloads here.</summary>
        public bool AllowDownload { get; init; } = true;

        /// <summary>Allow LIST here.</summary>
        public bool AllowList { get; init; } = true;

        /// <summary>Ratio factor for this dir (e.g. 3.0 = 1:3).</summary>
        public double Ratio { get; init; } = 0.0;

        /// <summary>If true, downloads are free (no ratio spent).</summary>
        public bool IsFree { get; init; }

        /// <summary>Multiplier for cost (e.g. 2x cost).</summary>
        public double MultiplyCost { get; init; } = 1.0;

        /// <summary>Additional upload bonus multiplier.</summary>
        public double UploadBonus { get; init; } = 0.0;

        public bool IsMatch(string virtualPath)
        {
            if (!Enabled)
                return false;

            if (string.IsNullOrWhiteSpace(virtualPath))
                return false;

            if (!virtualPath.StartsWith("/", StringComparison.Ordinal))
                virtualPath = "/" + virtualPath;

            var prefix = PathPrefix;
            if (!prefix.StartsWith("/", StringComparison.Ordinal))
                prefix = "/" + prefix;

            return virtualPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Compatibility ctor supporting named args like AllowUpload, Ratio, etc.
        /// </summary>
        public DirectoryRule(
            string SectionName,
            string VirtualPath,
            bool AllowUpload = true,
            bool AllowDownload = true,
            bool AllowList = true,
            double Ratio = 0.0,
            bool IsFree = false,
            double MultiplyCost = 1.0,
            double UploadBonus = 0.0,
            bool Enabled = true)
        {
            this.SectionName = SectionName;
            this.PathPrefix = VirtualPath;
            this.AllowUpload = AllowUpload;
            this.AllowDownload = AllowDownload;
            this.AllowList = AllowList;
            this.Ratio = Ratio;
            this.IsFree = IsFree;
            this.MultiplyCost = MultiplyCost;
            this.UploadBonus = UploadBonus;
            this.Enabled = Enabled;
        }

        public DirectoryRule()
        {
        }
    }
}
