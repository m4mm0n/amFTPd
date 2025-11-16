using amFTPd.Config.Ftpd;
using amFTPd.Db;

namespace amFTPd.Credits
{
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
        /// <remarks>This constructor sets up the <see cref="CreditEngine"/> with the necessary
        /// dependencies for handling user, group, and section operations. Ensure that all provided stores are properly
        /// initialized before passing them to this constructor.</remarks>
        /// <param name="users">The user store used to manage and retrieve user-related data.</param>
        /// <param name="groups">The group store used to manage and retrieve group-related data.</param>
        /// <param name="sections">The section store used to manage and retrieve section-related data.</param>
        public CreditEngine(IUserStore users, IGroupStore groups, ISectionStore sections)
        {
            _users = users;
            _groups = groups;
            _sections = sections;
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

            // Convert bytes → kilobytes (traditional credit model)
            var sizeKb = Math.Max(1, sizeBytes / 1024);

            // 1) Section multiplier
            var credits = sizeKb * sec.UploadMultiplier;

            // 2) Group override multiplier?
            if (grp != null && grp.SectionCredits.TryGetValue(sectionName, out var gmul))
            {
                DebugLog?.Invoke($"[CREDITS] Group multiplier for '{sectionName}' = {gmul}");
                credits = sizeKb * gmul;
            }

            // 3) User-level overrides in future (custom ratios)
            // TODO user specific rules if needed

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

            var sizeKb = Math.Max(1, sizeBytes / 1024);

            // Base section multiplier (cost)
            var cost = sizeKb * sec.DownloadMultiplier;

            // Group override cost
            if (grp != null && grp.SectionCredits.TryGetValue(sectionName, out var gmul))
            {
                DebugLog?.Invoke($"[CREDITS] Group DL multiplier for '{sectionName}' = {gmul}");
                cost = sizeKb * gmul;
            }

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
            return user.CreditsKb + earned;
        }

        // ====================================================================
        // INTERNALLY USED
        // ====================================================================

        private (Db.FtpSection? Section, FtpGroup? Group) ResolveSection(FtpUser user, string sectionName)
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
