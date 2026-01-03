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

    /// <summary>
    /// Determines whether the current session account has the required administrative or site operator permissions.
    /// </summary>
    /// <remarks>If both requiresAdmin and requiresSiteop are false, the method returns true for any
    /// authenticated account. If the session account is null, the method returns false.</remarks>
    /// <param name="ctx">The command context containing the session and account information to evaluate permissions for. Cannot be null.</param>
    /// <param name="requiresAdmin">true to require the account to have administrative privileges; otherwise, false.</param>
    /// <param name="requiresSiteop">true to require the account to have either administrative or site operator privileges; otherwise, false.</param>
    /// <returns>true if the account associated with the session meets the specified permission requirements; otherwise, false.</returns>
    protected static bool HasPermission(
        SiteCommandContext ctx,
        bool requiresAdmin,
        bool requiresSiteop)
    {
        var acc = ctx.Session.Account;
        if (acc is null)
            return false;

        if (requiresAdmin)
            return acc.IsAdmin;

        if (requiresSiteop)
            return acc.IsAdmin || acc.IsSiteop;

        return true;
    }
}