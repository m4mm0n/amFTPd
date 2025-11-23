/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
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

using System.Text;
using amFTPd.Config.Ftpd;

namespace amFTPd.Db;

/// <summary>
/// Provides functionality to repair and maintain the integrity of user, group, and section data stores.
/// </summary>
/// <remarks>The <see cref="DbRepair"/> class includes methods for repairing inconsistencies in user-group
/// links, group-section links, and section configurations. It also provides routines for sanitizing names,
/// rebuilding snapshot files, and addressing data integrity issues such as duplicate entries, invalid references,
/// and negative values. Debug logging can be enabled by assigning a delegate to the <see cref="DebugLog"/>
/// field.</remarks>
public static class DbRepair
{
    public static Action<string>? DebugLog;

    public sealed record RepairReport(
        List<string> Actions,
        List<string> Warnings
    )
    {
        public void AddAction(string msg)
        {
            Actions.Add(msg);
            DebugLog?.Invoke("[REPAIR] " + msg);
        }
        public void AddWarning(string msg)
        {
            Warnings.Add(msg);
            DebugLog?.Invoke("[REPAIR-WARN] " + msg);
        }
    }

    // =====================================================================
    // PUBLIC API — AUTO REPAIR ROUTINES
    // =====================================================================

    public static RepairReport RepairAll(
        IUserStore users,
        IGroupStore groups,
        ISectionStore sections,
        string baseDir,
        string masterPassword
    )
    {
        var rep = new RepairReport(new(), new());

        DebugLog?.Invoke("[REPAIR] Starting auto-repair.");

        RepairUserGroupLinks(users, groups, rep);
        RepairGroupUserList(users, groups, rep);
        RepairGroupSectionLinks(groups, sections, rep);
        RepairSections(sections, rep);
        RepairNaming(users, groups, sections, rep);

        // Rebuild snapshot files & reset WALs
        SnapshotRebuild("amftpd-users.db", baseDir, masterPassword, users, rep);
        SnapshotRebuild("amftpd-groups.db", baseDir, masterPassword, groups, rep);
        SnapshotRebuild("amftpd-sections.db", baseDir, masterPassword, sections, rep);

        DebugLog?.Invoke("[REPAIR] Completed.");

        return rep;
    }

    // =====================================================================
    // USER <-> GROUP LINK REPAIR
    // =====================================================================

    private static void RepairUserGroupLinks(
        IUserStore users,
        IGroupStore groups,
        RepairReport rep)
    {
        var groupNames = groups.GetAllGroups()
            .Select(g => g.GroupName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var u in users.GetAllUsers())
        {
            if (!string.IsNullOrEmpty(u.GroupName) &&
                !groupNames.Contains(u.GroupName))
            {
                rep.AddAction($"User '{u.UserName}' references missing group '{u.GroupName}', removing group assignment.");
                var repaired = u with { PrimaryGroup = null };

                users.TryUpdateUser(repaired, out _);
            }
        }
    }

    private static void RepairGroupUserList(
        IUserStore users,
        IGroupStore groups,
        RepairReport rep)
    {
        var userNames = users.GetAllUsers()
            .Select(u => u.UserName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups.GetAllGroups())
        {
            var badEntries = g.Users.Where(u => !userNames.Contains(u)).ToList();

            foreach (var b in badEntries)
            {
                rep.AddAction($"Removing non-existent user '{b}' from group '{g.GroupName}'.");
                g.Users.Remove(b);
            }

            // Remove duplicates
            var unique = g.Users.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (unique.Count != g.Users.Count)
            {
                rep.AddAction($"Removing duplicate users from group '{g.GroupName}'.");
                g.Users.Clear();
                g.Users.AddRange(unique);
            }

            groups.TryUpdateGroup(g, out _);
        }
    }

    // =====================================================================
    // GROUP <-> SECTION LINK REPAIR
    // =====================================================================

    private static void RepairGroupSectionLinks(
        IGroupStore groups,
        ISectionStore sections,
        RepairReport rep)
    {
        var validSections = sections.GetAllSections()
            .Select(s => s.SectionName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups.GetAllGroups())
        {
            var bad = g.SectionCredits
                .Where(kv => !validSections.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var b in bad)
            {
                rep.AddAction($"Removing invalid section '{b}' from group '{g.GroupName}'.");
                g.SectionCredits.Remove(b);
            }

            // Remove negative credits
            var neg = g.SectionCredits.Where(kv => kv.Value < 0).ToList();

            foreach (var kv in neg)
            {
                rep.AddWarning($"Negative credits for '{kv.Key}' in group '{g.GroupName}'. Resetting to 0.");
                g.SectionCredits[kv.Key] = 0;
            }

            groups.TryUpdateGroup(g, out _);
        }
    }

    // =====================================================================
    // SECTION REPAIRS
    // =====================================================================

    private static void RepairSections(ISectionStore sections, RepairReport rep)
    {
        var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in sections.GetAllSections())
        {
            // Path collisions
            if (pathMap.TryGetValue(s.RelativePath, out var other))
            {
                rep.AddWarning(
                    $"Sections '{s.SectionName}' and '{other}' share path '{s.RelativePath}'. Removing '{s.SectionName}'."
                );
                sections.TryDeleteSection(s.SectionName, out _);
                continue;
            }

            pathMap[s.RelativePath] = s.SectionName;

            // Repair multipliers
            if (s.UploadMultiplier < 0 || s.DownloadMultiplier < 0)
            {
                rep.AddWarning($"Fixing negative multipliers for section '{s.SectionName}'.");
                var repaired = s with
                {
                    UploadMultiplier = Math.Max(0, s.UploadMultiplier),
                    DownloadMultiplier = Math.Max(0, s.DownloadMultiplier)
                };
                sections.TryUpdateSection(repaired, out _);
            }

            // Repair empty names
            if (string.IsNullOrWhiteSpace(s.SectionName))
            {
                rep.AddWarning("Removing unnamed section.");
                sections.TryDeleteSection(s.SectionName, out _);
            }
        }
    }

    // =====================================================================
    // NAME SANITIZATION FOR USERS/GROUPS/SECTIONS
    // =====================================================================

    private static void RepairNaming(
        IUserStore users,
        IGroupStore groups,
        ISectionStore sections,
        RepairReport rep)
    {
        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                if (!char.IsControl(c) && c != '\0')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        // Users
        foreach (var u in users.GetAllUsers())
        {
            var repaired = Sanitize(u.UserName);
            if (repaired != u.UserName)
            {
                rep.AddWarning($"Sanitizing username '{u.UserName}' → '{repaired}'.");
                var nu = u with { UserName = repaired };
                users.TryUpdateUser(nu, out _);
            }
        }

        // Groups
        foreach (var g in groups.GetAllGroups())
        {
            var repaired = Sanitize(g.GroupName);
            if (repaired != g.GroupName)
            {
                rep.AddWarning($"Sanitizing group '{g.GroupName}' → '{repaired}'.");
                groups.TryRenameGroup(g.GroupName, repaired, out _);
            }
        }

        // Sections
        foreach (var s in sections.GetAllSections())
        {
            var repaired = Sanitize(s.SectionName);
            if (repaired != s.SectionName)
            {
                rep.AddWarning($"Sanitizing section '{s.SectionName}' → '{repaired}'.");
                var ns = s with { SectionName = repaired };
                sections.TryUpdateSection(ns, out _);
            }
        }
    }

    // =====================================================================
    // SNAPSHOT REBUILD
    // =====================================================================

    private static void SnapshotRebuild(
        string fileName,
        string baseDir,
        string masterPassword,
        object store,
        RepairReport rep)
    {
        var db = Path.Combine(baseDir, fileName);

        rep.AddAction($"Rebuilding snapshot '{db}'…");

        if (store is BinaryUserStore bu)
        {
            bu.ForceSnapshotRewrite();
            return;
        }
        if (store is BinaryGroupStore bg)
        {
            bg.ForceSnapshotRewrite();
            return;
        }
        if (store is BinarySectionStore bs)
        {
            bs.ForceSnapshotRewrite();
            return;
        }

        rep.AddWarning($"Store type {store.GetType().Name} has no snapshot rebuild support.");
    }
}