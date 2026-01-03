namespace amFTPd.Core.ReleaseSystem;

/// <summary>
/// Specifies the possible states of a release within the system.
/// </summary>
/// <remarks>Use this enumeration to represent and track the current status of a release, such as whether it is
/// new, incomplete, complete, nuked, or archived. The specific meaning of each state may depend on the application's
/// domain logic.</remarks>
public enum ReleaseState
{
    New,
    Incomplete,
    Complete,
    Nuked,
    Archived
}