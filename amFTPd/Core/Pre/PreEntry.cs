using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace amFTPd.Core.Pre;

/// <summary>
/// Represents a record of a pre-release entry, including metadata such as section, release name, virtual path, user,
/// and timestamp.
/// </summary>
/// <param name="Section">The name of the section to which the pre-release entry belongs. This value identifies the logical grouping or
/// category for the entry.</param>
/// <param name="ReleaseName">The name of the release associated with the pre-release entry. This value typically corresponds to a version or
/// identifier for the release.</param>
/// <param name="VirtualPath">The virtual path representing the location or resource associated with the pre-release entry. This value is used to
/// reference the entry within a virtualized or abstracted file system.</param>
/// <param name="User">The name of the user who created or is associated with the pre-release entry. This value identifies the originator
/// of the entry.</param>
/// <param name="Timestamp">The date and time, in coordinated universal time (UTC), when the pre-release entry was created or recorded.</param>
public sealed record PreEntry(
    string Section,
    string ReleaseName,
    string VirtualPath,
    string User,
    DateTimeOffset Timestamp);