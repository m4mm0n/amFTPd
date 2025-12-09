/*
* ====================================================================================================
*  Project:        amFTPd - a managed FTP daemon
*  Author:         Geir Gustavsen, ZeroLinez Softworx
*  Created:        2025-11-25
*  Last Modified:  2025-11-25
*  
*  License:
*      MIT License
*      https://opensource.org/licenses/MIT
*
*  Notes:
*      Simple in-memory implementation of ISectionStore. This is used when the
*      binary DB backend is not active, or as a lightweight wrapper over the
*      configuration-based SectionManager.
* ====================================================================================================
*/

namespace amFTPd.Core.Site;

/// <summary>
/// Base class for all SITE commands.
/// </summary>
public abstract class SiteCommandBase
{
    /// <summary>Command name, e.g. "HELP", "ADDUSER".</summary>
    public abstract string Name { get; }

    /// <summary>Whether this SITE command requires an admin account.</summary>
    public virtual bool RequiresAdmin => false;

    /// <summary>Optional short help line (for SITE HELP).</summary>
    public virtual string HelpText => Name;

    /// <summary>Execute the SITE command.</summary>
    public abstract Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken);
}