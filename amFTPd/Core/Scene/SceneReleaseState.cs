namespace amFTPd.Core.Scene;

/// <summary>
/// Represents the release state of a scene, including its section, path, pre-release status, nuke status, and last
/// modification time.
/// </summary>
/// <param name="Section">The name of the section or category to which the scene belongs. Cannot be null.</param>
/// <param name="VirtualPath">The virtual path identifying the scene's location within the system. Cannot be null.</param>
/// <param name="IsPre">true if the scene has been pre-released; otherwise, false.</param>
/// <param name="IsCompleted">true if the scene has been completed; otherwise, false.</param>
/// <param name="IsNuked">true if the scene has been nuked; otherwise, false.</param>
/// <param name="NukeReason">The reason the scene was nuked, or null if the scene is not nuked.</param>
/// <param name="LastChanged">The date and time, in UTC, when the release state was last changed.</param>
public sealed record SceneReleaseState(
    string Section,
    string Path,
    bool IsPre,
    bool IsCompleted,
    bool IsNuked,
    string? NukeReason,
    DateTimeOffset LastChanged);