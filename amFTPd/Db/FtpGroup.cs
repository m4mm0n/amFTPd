namespace amFTPd.Db
{
    /// <summary>
    /// Represents a group in an FTP system, including its name, description, associated users, and section-specific
    /// credits.
    /// </summary>
    /// <param name="GroupName">The name of the FTP group. This value cannot be null or empty.</param>
    /// <param name="Description">A brief description of the FTP group. This value can be null or empty.</param>
    /// <param name="Users">A list of usernames associated with the group. This list cannot be null but may be empty.</param>
    /// <param name="SectionCredits">A dictionary mapping section names to their respective credit values for the group. The dictionary cannot be
    /// null but may be empty. Keys represent section names, and values represent the credits.</param>
    public sealed record FtpGroup(
        string GroupName,
        string Description,
        List<string> Users,
        Dictionary<string, long> SectionCredits
    );
}
