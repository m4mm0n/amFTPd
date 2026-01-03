namespace amFTPd.Core.Import.Records;

/// <summary>
/// Represents a user record imported from an external source, including group memberships, roles, and credit
/// information.
/// </summary>
/// <param name="UserName">The user name associated with the imported user record. Cannot be null.</param>
/// <param name="PrimaryGroup">The name of the primary group to which the user belongs. Cannot be null.</param>
/// <param name="SecondaryGroups">A read-only list of names of secondary groups the user is a member of. Cannot be null; may be empty if the user has
/// no secondary groups.</param>
/// <param name="IsSiteop">true if the user has site operator privileges; otherwise, false.</param>
/// <param name="IsAdmin">true if the user has administrative privileges; otherwise, false.</param>
/// <param name="IsNoRatio">true if the user is exempt from ratio requirements; otherwise, false.</param>
/// <param name="CreditsKb">The number of credits assigned to the user, measured in kilobytes. Must be zero or greater.</param>
public sealed record ImportedUserRecord(
    string UserName,
    string PrimaryGroup,
    IReadOnlyList<string> SecondaryGroups,
    bool IsSiteop,
    bool IsAdmin,
    bool IsNoRatio,
    long CreditsKb);