/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-23
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

namespace amFTPd.Config.Ftpd.RatioRules
{
    /// <summary>
    /// Represents a set of access and ratio rules that can be applied to a directory,
    /// including upload/download permissions, free-leech, cost multipliers, and ratio overrides.
    /// </summary>
    /// <remarks>
    /// All properties are optional (<c>null</c> = no explicit rule at this level).
    /// This lets you define partial overrides on directory trees.
    /// </remarks>
    /// <param name="AllowUpload">
    /// If <c>false</c>, uploads are not allowed in this directory subtree.
    /// If <c>true</c>, uploads are explicitly allowed. If <c>null</c>, inherit default/global behavior.
    /// </param>
    /// <param name="AllowDownload">
    /// If <c>false</c>, downloads are not allowed in this directory subtree.
    /// If <c>true</c>, downloads are explicitly allowed. If <c>null</c>, inherit default/global behavior.
    /// </param>
    /// <param name="IsFree">
    /// If <c>true</c>, transfers in this subtree are free-leech (no credit cost).
    /// If <c>false</c>, normal ratio applies. If <c>null</c>, inherit.
    /// </param>
    /// <param name="MultiplyCost">
    /// Optional multiplier applied to cost (e.g. 0.5 = half cost, 2.0 = double cost).
    /// </param>
    /// <param name="UploadBonus">
    /// Optional multiplier applied to upload credit earnings (e.g. 2.0 = double credits).
    /// </param>
    /// <param name="Ratio">
    /// Optional ratio override for this subtree (e.g. 3.0 = 1:3, 1.0 = 1:1, etc.).
    /// </param>
    /// <param name="AllowList">
    /// If <c>false</c>, directory listings (LIST/NLST/MLSD/MLST) are not allowed in this subtree.
    /// If <c>true</c>, they are explicitly allowed. If <c>null</c>, inherit default behavior.
    /// </param>
    public sealed record DirectoryRule(
        bool? AllowUpload,
        bool? AllowDownload,
        bool? IsFree,
        double? MultiplyCost,
        double? UploadBonus,
        double? Ratio,
        bool? AllowList
    )
    {
        /// <summary>
        /// Gets an empty directory rule instance with all properties unset
        /// (no explicit restrictions; everything inherits from higher levels / defaults).
        /// </summary>
        public static DirectoryRule Empty { get; } = new(
            AllowUpload: null,
            AllowDownload: null,
            IsFree: null,
            MultiplyCost: null,
            UploadBonus: null,
            Ratio: null,
            AllowList: null
        );
    }
}
