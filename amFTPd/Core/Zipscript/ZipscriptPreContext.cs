namespace amFTPd.Core.Zipscript;

/// <summary>
/// Represents the context information provided to a Zipscript pre-processing operation, including section, release,
/// user, and timestamp details.
/// </summary>
/// <param name="SectionName">The name of the section in which the release is being processed. Cannot be null or empty.</param>
/// <param name="ReleaseName">The name of the release associated with the operation. Cannot be null or empty.</param>
/// <param name="VirtualReleasePath">The virtual path to the release within the section. Cannot be null or empty.</param>
/// <param name="UserName">The name of the user initiating the operation. Cannot be null or empty.</param>
/// <param name="Timestamp">The date and time, in coordinated universal time (UTC), when the context was created.</param>
public sealed record ZipscriptPreContext(
    string SectionName,
    string ReleaseName,
    string VirtualReleasePath,
    string UserName,
    DateTimeOffset Timestamp);