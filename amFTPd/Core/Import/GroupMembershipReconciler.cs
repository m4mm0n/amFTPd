using amFTPd.Config.Ftpd;
using amFTPd.Db;

namespace amFTPd.Core.Import;

/// <summary>
/// Provides functionality to reconcile user group memberships by ensuring that each user's primary and secondary group
/// associations are reflected in the group store.
/// </summary>
/// <remarks>Use this class to synchronize group membership data between user and group stores, ensuring that all
/// users are members of their specified primary and secondary groups. This class is not thread-safe; if used
/// concurrently, callers should provide their own synchronization.</remarks>
public sealed class GroupMembershipReconciler
{
    public void Apply(
        IUserStore users,
        IGroupStore groups)
    {
        foreach (var user in users.GetAllUsers())
        {
            // Primary group
            if (!string.IsNullOrWhiteSpace(user.PrimaryGroup))
            {
                AddUserToGroup(user.UserName, user.PrimaryGroup, groups);
            }

            // Secondary groups
            foreach (var g in user.SecondaryGroups)
            {
                if (!string.IsNullOrWhiteSpace(g))
                    AddUserToGroup(user.UserName, g, groups);
            }
        }
    }

    private static void AddUserToGroup(
        string userName,
        string groupName,
        IGroupStore groups)
    {
        var group = groups.FindGroup(groupName);
        if (group is null)
            return;

        if (group.Users.Contains(userName,
                StringComparer.OrdinalIgnoreCase))
            return;

        var updated = group with
        {
            Users = new List<string>(group.Users) { userName }
        };

        groups.TryUpdateGroup(updated, out _);
    }
}
