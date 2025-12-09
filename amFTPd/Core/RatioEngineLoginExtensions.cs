/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RatioEngineLoginExtensions.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-08 03:59:21
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x91B98528
 *  
 *  Description:
 *      Extension methods for <see cref="RatioEngine"/> related to login handling.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Core.Ratio;
using amFTPd.Scripting;

namespace amFTPd.Core;

/// <summary>
/// Extension methods for <see cref="RatioEngine"/> related to login handling.
/// </summary>
public static class RatioEngineLoginExtensions
{
    /// <summary>
    /// Resolves the login rule for the specified <see cref="RatioLoginContext"/> using the provided <see
    /// cref="RatioEngine"/>.
    /// </summary>
    /// <param name="ratioEngine">The <see cref="RatioEngine"/> instance used to evaluate the login rule. Cannot be <see langword="null"/>.</param>
    /// <param name="context">The <see cref="RatioLoginContext"/> containing the login context information. Cannot be <see langword="null"/>.</param>
    /// <returns>An <see cref="AMScriptResult"/> indicating the result of the login rule evaluation, including the action to take
    /// and any associated adjustments.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="ratioEngine"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static AMScriptResult ResolveLoginRule(this RatioEngine ratioEngine, RatioLoginContext context)
    {
        if (ratioEngine is null) throw new ArgumentNullException(nameof(ratioEngine));
        if (context is null) throw new ArgumentNullException(nameof(context));

        // TODO: implement real login rules based on AMScript/ratio later.
        // For now: always allow login, no cost/earned adjustments, no limits.
        return new AMScriptResult(
            AMRuleAction.Allow,
            0L,
            0L
        );
    }
}