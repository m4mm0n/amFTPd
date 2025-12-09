/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-12-01
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

using amFTPd.Config.Ftpd;
using amFTPd.Db;

namespace amFTPd.Credits
{
    public sealed record DownloadDecision(
        bool Allowed,
        long RequiredCredits,
        long NewBalance);

    /// <summary>
    /// Provides functionality for calculating and managing user credits based on file uploads and downloads.
    /// </summary>
    /// <remarks>The <see cref="CreditEngine"/> class is designed to compute credits earned for file uploads
    /// and credits  consumed for file downloads, based on configurable multipliers at the section and group levels. It
    /// also  provides methods to check and update user credit balances. This class is intended for use in systems 
    /// where user activity is tracked and rewarded or restricted based on credit balances.</remarks>
    public sealed class CreditEngine
    {
        private readonly IUserStore _users;
        private readonly IGroupStore _groups;
        private readonly ISectionStore _sections;

        public Action<string>? DebugLog;

        /// <summary>
        /// Initializes a new instance of the <see cref="CreditEngine"/> class with the specified user, group, and
        /// section stores.
        /// </summary>
        public CreditEngine(IUserStore users, IGroupStore groups, ISectionStore sections)
        {
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
            _sections = sections ?? throw new ArgumentNullException(nameof(sections));
        }

        // ====================================================================
        // PUBLIC ENTRYPOINTS
        // ====================================================================

        /// <summary>
        /// Compute credits earned for an upload of a given byte size.
        /// </summary>
        public long ComputeUploadCredits(FtpUser user, string sectionName, long sizeBytes)
        {
            var (sec, grp) = ResolveSection(user, sectionName);
            if (sec is null)
                return 0;

            // bytes → kilobytes (traditional credit model)
            var sizeKb = Math.Max(1L, sizeBytes / 1024L);

            // 1) Section multiplier
            double multiplier = sec.UploadMultiplier;

            // 2) Group override multiplier?
            if (grp != null && grp.SectionCredits.TryGetValue(sectionName, out var gmul))
            {
                DebugLog?.Invoke($"[CREDITS] Group UL multiplier for '{sectionName}' = {gmul}");
                multiplier = gmul;
            }

            // Explicit double -> long conversion (floor credits)
            var credits = (long)Math.Floor(sizeKb * multiplier);
            if (credits < 0) credits = 0;

            return credits;
        }

        /// <summary>
        /// Compute credits cost for a download of a given byte size.
        /// </summary>
        public long ComputeDownloadCost(FtpUser user, string sectionName, long sizeBytes)
        {
            var (sec, grp) = ResolveSection(user, sectionName);
            if (sec is null)
                return 0;

            var sizeKb = Math.Max(1L, sizeBytes / 1024L);

            // Base section multiplier (cost)
            double multiplier = sec.DownloadMultiplier;

            // Group override cost
            if (grp != null && grp.SectionCredits.TryGetValue(sectionName, out var gmul))
            {
                DebugLog?.Invoke($"[CREDITS] Group DL multiplier for '{sectionName}' = {gmul}");
                multiplier = gmul;
            }

            var cost = (long)Math.Floor(sizeKb * multiplier);
            if (cost < 0) cost = 0;

            return cost;
        }

        /// <summary>
        /// Check whether a user has enough credits to download a file.
        /// </summary>
        public bool CanDownload(FtpUser user, string sectionName, long sizeBytes)
        {
            var cost = ComputeDownloadCost(user, sectionName, sizeBytes);
            return user.CreditsKb >= cost;
        }

        /// <summary>
        /// Decrement credits after download.
        /// </summary>
        public bool TryConsumeCredits(FtpUser user, string sectionName, long sizeBytes, out long newCredits)
        {
            var cost = ComputeDownloadCost(user, sectionName, sizeBytes);
            if (user.CreditsKb < cost)
            {
                newCredits = user.CreditsKb;
                return false;
            }

            newCredits = user.CreditsKb - cost;
            return true;
        }

        /// <summary>
        /// Add credits after upload.
        /// </summary>
        public long AwardCredits(FtpUser user, string sectionName, long sizeBytes)
        {
            var earned = ComputeUploadCredits(user, sectionName, sizeBytes);
            var total = user.CreditsKb + earned;

            // simple overflow guard (very defensive)
            if (total < 0) total = long.MaxValue;

            return total;
        }

        // ====================================================================
        // INTERNALLY USED
        // ====================================================================

        private (Config.Ftpd.FtpSection? Section, FtpGroup? Group) ResolveSection(FtpUser user, string sectionName)
        {
            var section = _sections.FindSection(sectionName);
            if (section == null)
            {
                DebugLog?.Invoke($"[CREDITS] Section '{sectionName}' not found.");
                return (null, null);
            }

            FtpGroup? group = null;

            if (!string.IsNullOrWhiteSpace(user.GroupName))
                group = _groups.FindGroup(user.GroupName!);

            return (section, group);
        }
    }
}
