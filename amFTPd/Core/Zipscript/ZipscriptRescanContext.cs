/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptRescanContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 09:03:41
 *  Last Modified:  2025-12-14 09:33:53
 *  CRC32:          0xE103370D
 *  
 *  Description:
 *      Context passed when a release directory should be fully rescanned.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Core.Zipscript;

/// <summary>
/// Context passed when a release directory should be fully rescanned.
/// </summary>
/// <param name="SectionName">The section the release belongs to.</param>
/// <param name="VirtualReleasePath">Virtual path of the release directory.</param>
/// <param name="PhysicalReleasePath">Physical path of the release directory.</param>
/// <param name="UserName">User that triggered the rescan, if any.</param>
/// <param name="IncludeSubdirs">Whether to walk sub–directories as well.</param>
/// <param name="RequestedAt">Timestamp for when the rescan was requested.</param>
public sealed record ZipscriptRescanContext(
    string SectionName,
    string VirtualReleasePath,
    string? PhysicalReleasePath,
    string? UserName,
    bool IncludeSubdirs,
    DateTimeOffset RequestedAt
);