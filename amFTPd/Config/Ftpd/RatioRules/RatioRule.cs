/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RatioRule.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-13 20:18:12
 *  CRC32:          0x6F8EC2AF
 *  
 *  Description:
 *      Describes ratio behavior for a logical section or group.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */






namespace amFTPd.Config.Ftpd.RatioRules
{
    /// <summary>
    /// Describes ratio behavior for a logical section or group.
    /// </summary>
    public sealed record RatioRule
    {
        /// <summary>Rule name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Credits per KiB uploaded.</summary>
        public int CreditsPerKiBUploaded { get; init; }

        /// <summary>Credits per KiB downloaded.</summary>
        public int CreditsPerKiBDownloaded { get; init; }

        /// <summary>Minimum ratio to enforce (null disables).</summary>
        public double? MinimumRatio { get; init; }

        /// <summary>If true, downloads are free (no deduction).</summary>
        public bool? IsFree { get; init; }

        /// <summary>Ratio factor (e.g. 3.0 = 1:3).</summary>
        public double? Ratio { get; init; }

        /// <summary>Cost multiplier.</summary>
        public double? MultiplyCost { get; init; } = 1.0;

        /// <summary>Extra upload bonus multiplier.</summary>
        public double? UploadBonus { get; init; } = 0.0;

        /// <summary>Time multiplier for this rule (null = default).</summary>
        public double? TimeMultiplier { get; init; }

        public int MinHour { get; init; }

        public int MaxHour { get; init; }

        public long CalculateUploadCredits(long sizeInBytes)
        {
            if (sizeInBytes <= 0)
                return 0;

            var kib = sizeInBytes / 1024.0;
            return (long)(kib * CreditsPerKiBUploaded);
        }

        public long CalculateDownloadCost(long sizeInBytes)
        {
            if (IsFree != null && (IsFree.Value || sizeInBytes <= 0))
                return 0;

            var kib = sizeInBytes / 1024.0;
            return (long)(kib * CreditsPerKiBDownloaded * MultiplyCost)!;
        }

        public RatioRule()
        {
        }

        /// <summary>
        /// Compatibility ctor – named arg "Ratio" + flags like IsFree, MultiplyCost, UploadBonus.
        /// </summary>
        public RatioRule(
            string Name,
            double Ratio,
            double MultiplyCost = 1.0,
            bool IsFree = false,
            double UploadBonus = 0.0,
            int CreditsPerKiBUploaded = 0,
            int CreditsPerKiBDownloaded = 0,
            double? MinimumRatio = null)
        {
            this.Name = Name;
            this.Ratio = Ratio;
            this.MultiplyCost = MultiplyCost;
            this.IsFree = IsFree;
            this.UploadBonus = UploadBonus;
            this.CreditsPerKiBUploaded = CreditsPerKiBUploaded;
            this.CreditsPerKiBDownloaded = CreditsPerKiBDownloaded;
            this.MinimumRatio = MinimumRatio;
        }

        public RatioRule(
            string Name,
            double? Ratio,
            double? MultiplyCost,
            bool? IsFree,
            double? UploadBonus,
            int MinHour,
            int MaxHour,
            double? TimeMultiplier)
        {
            this.Name = Name;
            this.Ratio = Ratio;
            this.MultiplyCost = MultiplyCost;
            this.IsFree = IsFree;
            this.UploadBonus = UploadBonus;
            this.MinHour = MinHour;
            this.MaxHour = MaxHour;
            this.TimeMultiplier = TimeMultiplier;
        }
        public RatioRule(
            double? Ratio,
            double? MultiplyCost,
            bool? IsFree,
            double? UploadBonus)
            : this(
                Name: string.Empty,
                Ratio: Ratio,
                MultiplyCost: MultiplyCost,
                IsFree: IsFree,
                UploadBonus: UploadBonus,
                MinHour: 0,
                MaxHour: 24,
                TimeMultiplier: null)
        {
        }
    }
}
