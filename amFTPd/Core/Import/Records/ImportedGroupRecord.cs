namespace amFTPd.Core.Import.Records;

/// <summary>
/// Represents a group record imported from an external source, including its name, default ratio, and privilege status.
/// </summary>
/// <param name="GroupName">The name of the group as defined in the external source. Cannot be null or empty.</param>
/// <param name="DefaultRatio">The default ratio associated with the group. Must be a non-negative value.</param>
/// <param name="IsPrivileged">A value indicating whether the group has privileged status. Set to <see langword="true"/> if the group is
/// privileged; otherwise, <see langword="false"/>.</param>
public sealed record ImportedGroupRecord(
    string GroupName,
    double DefaultRatio,
    bool IsPrivileged);