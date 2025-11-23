/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-22
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
    /// Represents a rule that defines ratio-based parameters for cost calculation and bonus allocation.
    /// </summary>
    /// <remarks>Use <see cref="Default"/> to obtain a standard ratio rule with default values. This record is
    /// immutable and thread-safe.</remarks>
    /// <param name="Ratio">The ratio value used to determine cost adjustments and bonus calculations. Must be greater than or equal to
    /// zero.</param>
    /// <param name="IsFree">Indicates whether the rule applies a free cost. Set to <see langword="true"/> to waive associated costs;
    /// otherwise, <see langword="false"/>.</param>
    /// <param name="MultiplyCost">The multiplier applied to the base cost when calculating the final cost under this rule. Must be greater than or
    /// equal to zero.</param>
    /// <param name="UploadBonus">The bonus value awarded for uploads when this rule is applied. Must be greater than or equal to zero.</param>
    public sealed record RatioRule(
        double Ratio,
        bool IsFree,
        double MultiplyCost,
        double UploadBonus
    )
    {
        /// <summary>
        /// Gets the default ratio rule configuration used when no custom rule is specified.
        /// </summary>
        /// <remarks>Use this property to obtain a standard ratio rule with typical values for general
        /// scenarios. The returned instance is immutable and can be shared safely across threads.</remarks>
        public static RatioRule Default { get; } = new(
            Ratio: 1.0,
            IsFree: false,
            MultiplyCost: 1.0,
            UploadBonus: 1.0
        );
    }
}
