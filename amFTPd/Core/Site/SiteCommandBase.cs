/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteCommandBase.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 00:28:50
 *  Last Modified:  2025-12-14 21:23:52
 *  CRC32:          0xFD79CA07
 *  
 *  Description:
 *      Base class for all SITE commands.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
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

    /// <summary>Whether this SITE command requires at least a SiteOp (or admin).</summary>
    public virtual bool RequiresSiteop => false;

    /// <summary>Optional short help line (for SITE HELP).</summary>
    public virtual string HelpText => Name;

    /// <summary>Execute the SITE command.</summary>
    public abstract Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken);
}