/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           DbFsckDeep.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 20:22:05
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xF3734F7C
 *  
 *  Description:
 *      Provides functionality to perform a comprehensive integrity check across users, groups, and sections in a database-li...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using amFTPd.Config.Ftpd;

namespace amFTPd.Db
{
    /// <summary>
    /// Provides functionality to perform a comprehensive integrity check across users, groups, and sections in a
    /// database-like system, ensuring logical consistency and identifying potential issues.
    /// </summary>
    /// <remarks>The <see cref="DbFsckDeep"/> class is designed to validate the relationships and data
    /// integrity between users, groups, and sections. It performs checks such as verifying references, ensuring naming
    /// rules are followed, and detecting invalid or inconsistent data. The results of the validation are returned as a
    /// <see cref="DeepFsckResult"/>, which includes lists of errors and warnings. <para> This class is intended for use
    /// in scenarios where a deep validation of the system's data is required, such as during maintenance operations or
    /// before deploying changes to production. </para> <para> The class also supports optional debug logging via the
    /// <see cref="DebugLog"/> delegate, which can be used to capture detailed information about the validation process.
    /// </para></remarks>
    public static class DbFsckDeep
    {
        public static Action<string>? DebugLog;

        public sealed record DeepFsckResult(
            List<string> Errors,
            List<string> Warnings
        )
        {
            public bool Success => Errors.Count == 0;

            public void AddError(string msg)
            {
                Errors.Add(msg);
                DebugLog?.Invoke("[FSCK-DEEP] ERROR: " + msg);
            }

            public void AddWarning(string msg)
            {
                Warnings.Add(msg);
                DebugLog?.Invoke("[FSCK-DEEP] WARN: " + msg);
            }
        }

        // ================================================================
        // PUBLIC ENTRYPOINT
        // ================================================================
        public static DeepFsckResult CheckAll(
            IUserStore users,
            IGroupStore groups,
            ISectionStore sections
        )
        {
            var res = new DeepFsckResult(new(), new());

            DebugLog?.Invoke("[FSCK-DEEP] Starting deep validation…");

            CheckUserGroupLinks(users, groups, res);
            CheckGroupUserLinks(users, groups, res);
            CheckGroupSectionLinks(groups, sections, res);
            CheckSectionValidity(sections, res);

            CheckNamingRules(users, groups, sections, res);
            CheckCredits(sections, groups, res);

            DebugLog?.Invoke("[FSCK-DEEP] Completed.");

            return res;
        }

        // ================================================================
        // SECTION 1 — USERS ↔ GROUPS
        // ================================================================

        private static void CheckUserGroupLinks(
            IUserStore users,
            IGroupStore groups,
            DeepFsckResult res)
        {
            var groupMap = groups.GetAllGroups()
                                 .ToDictionary(g => g.GroupName, StringComparer.OrdinalIgnoreCase);

            foreach (var u in users.GetAllUsers())
            {
                if (!string.IsNullOrEmpty(u.GroupName))
                {
                    if (!groupMap.ContainsKey(u.GroupName))
                        res.AddError($"User '{u.UserName}' references missing group '{u.GroupName}'.");
                }
            }
        }

        private static void CheckGroupUserLinks(
            IUserStore users,
            IGroupStore groups,
            DeepFsckResult res)
        {
            var userMap = users.GetAllUsers()
                               .ToDictionary(u => u.UserName, StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups.GetAllGroups())
            {
                foreach (var u in g.Users)
                {
                    if (!userMap.ContainsKey(u))
                        res.AddError($"Group '{g.GroupName}' references unknown user '{u}'.");
                }
            }
        }

        // ================================================================
        // SECTION 2 — GROUPS ↔ SECTIONS
        // ================================================================

        private static void CheckGroupSectionLinks(
            IGroupStore groups,
            ISectionStore sections,
            DeepFsckResult res)
        {
            var sectionNames = sections.GetAllSections()
                                       .Select(s => s.SectionName)
                                       .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups.GetAllGroups())
            {
                foreach (var kv in g.SectionCredits)
                {
                    if (!sectionNames.Contains(kv.Key))
                        res.AddError($"Group '{g.GroupName}' references unknown section '{kv.Key}'.");
                }
            }
        }

        // ================================================================
        // SECTION 3 — SECTION VALIDITY
        // ================================================================

        private static void CheckSectionValidity(ISectionStore sections, DeepFsckResult res)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in sections.GetAllSections())
            {
                if (string.IsNullOrWhiteSpace(s.SectionName))
                    res.AddError("Section with empty name.");

                if (string.IsNullOrWhiteSpace(s.RelativePath))
                    res.AddError($"Section '{s.SectionName}' has empty path.");

                // Ensure no 2 sections share same path
                if (paths.TryGetValue(s.RelativePath, out var other))
                {
                    res.AddError($"Sections '{s.SectionName}' and '{other}' share same path '{s.RelativePath}'.");
                }
                else
                {
                    paths[s.RelativePath] = s.SectionName;
                }

                // Check multipliers
                if (s.UploadMultiplier < 0)
                    res.AddWarning($"Section '{s.SectionName}' has negative upload multiplier.");

                if (s.DownloadMultiplier < 0)
                    res.AddWarning($"Section '{s.SectionName}' has negative download multiplier.");
            }
        }

        // ================================================================
        // SECTION 4 — NAME RULES (UTF-8 validity, illegal chars)
        // ================================================================

        private static void CheckNamingRules(
            IUserStore users,
            IGroupStore groups,
            ISectionStore sections,
            DeepFsckResult res)
        {
            foreach (var u in users.GetAllUsers())
            {
                if (!IsValidName(u.UserName))
                    res.AddWarning($"User '{u.UserName}' contains unusual or invalid characters.");

                if (u.GroupName != null && !IsValidName(u.GroupName))
                    res.AddWarning($"User '{u.UserName}' has group '{u.GroupName}' with invalid characters.");
            }

            foreach (var g in groups.GetAllGroups())
            {
                if (!IsValidName(g.GroupName))
                    res.AddWarning($"Group '{g.GroupName}' contains invalid characters.");

                foreach (var u in g.Users)
                    if (!IsValidName(u))
                        res.AddWarning($"Group '{g.GroupName}' contains invalid member username '{u}'.");
            }

            foreach (var s in sections.GetAllSections())
            {
                if (!IsValidName(s.SectionName))
                    res.AddWarning($"Section '{s.SectionName}' has invalid characters.");
            }
        }

        private static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            foreach (var c in name)
            {
                if (char.IsControl(c) || c == '\0')
                    return false;
            }
            return true;
        }

        // ================================================================
        // SECTION 5 — CREDITS / LOGICAL CONSISTENCY
        // ================================================================

        private static void CheckCredits(ISectionStore sections, IGroupStore groups, DeepFsckResult res)
        {
            foreach (var g in groups.GetAllGroups())
            {
                foreach (var kv in g.SectionCredits)
                {
                    if (kv.Value < 0)
                        res.AddWarning($"Group '{g.GroupName}' has negative credit value in section '{kv.Key}'.");
                }
            }
        }
    }
}
