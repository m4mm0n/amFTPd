/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IdentConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-13 04:32:32
 *  CRC32:          0xE7F2C110
 *  
 *  Description:
 *      Represents the configuration settings for IDENT-based authentication and authorization.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







namespace amFTPd.Config.Ident;

/// <summary>
/// Represents the configuration settings for IDENT-based authentication and authorization.
/// </summary>
/// <remarks>This configuration defines the behavior of IDENT queries, caching, and various validation
/// checks such as strict matching, reverse DNS validation, and TLS binding verification. It also allows
/// customization of group mappings and timeout settings for IDENT operations.</remarks>
public sealed record IdentConfig
{
    /// <summary>
    /// Gets the identification modes that are enabled for this instance.
    /// </summary>
    /// <remarks>The value is a combination of one or more <see cref="IdentMode"/> flags. Use bitwise
    /// operations to check for specific modes. This property is set during object initialization and cannot be modified
    /// afterwards.</remarks>
    public IdentMode Modes { get; init; } = IdentMode.Standard | IdentMode.LoggingOnly | IdentMode.Caching;

    /// <summary>IDENT query timeout in milliseconds.</summary>
    public int TimeoutMs { get; init; } = 3000;

    /// <summary>How long cache entries live (seconds).</summary>
    public int CacheTtlSeconds { get; init; } = 300;

    /// <summary>Optional group mappings based on IDENT username.</summary>
    public List<IdentGroupMapping> GroupMappings { get; init; } = [];

    /// <summary>If true and strict match fails, deny login immediately.</summary>
    public bool DenyOnStrictMismatch { get; init; } = true;

    /// <summary>If true and reverse DNS check fails, deny login.</summary>
    public bool DenyOnReverseDnsMismatch { get; init; } = false;

    /// <summary>If true and TLS binding check fails, deny login.</summary>
    public bool DenyOnTlsBindingMismatch { get; init; } = false;
}