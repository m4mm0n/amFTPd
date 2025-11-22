/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-22
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

namespace amFTPd.Config.Ident;

/// <summary>
/// Represents the configuration settings for IDENT-based authentication and authorization.
/// </summary>
/// <remarks>This configuration defines the behavior of IDENT queries, caching, and various validation
/// checks such as strict matching, reverse DNS validation, and TLS binding verification. It also allows
/// customization of group mappings and timeout settings for IDENT operations.</remarks>
public sealed record IdentConfig
{
    public IdentMode Modes { get; init; } = IdentMode.Standard | IdentMode.LoggingOnly | IdentMode.Caching;

    /// <summary>IDENT query timeout in milliseconds.</summary>
    public int TimeoutMs { get; init; } = 3000;

    /// <summary>How long cache entries live (seconds).</summary>
    public int CacheTtlSeconds { get; init; } = 300;

    /// <summary>Optional group mappings based on IDENT username.</summary>
    public List<IdentGroupMapping> GroupMappings { get; init; } = new();

    /// <summary>If true and strict match fails, deny login immediately.</summary>
    public bool DenyOnStrictMismatch { get; init; } = true;

    /// <summary>If true and reverse DNS check fails, deny login.</summary>
    public bool DenyOnReverseDnsMismatch { get; init; } = false;

    /// <summary>If true and TLS binding check fails, deny login.</summary>
    public bool DenyOnTlsBindingMismatch { get; init; } = false;
}