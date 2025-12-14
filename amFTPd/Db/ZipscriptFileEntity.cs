/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptFileEntity.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 09:04:15
 *  Last Modified:  2025-12-14 10:55:45
 *  CRC32:          0x592EECEB
 *  
 *  Description:
 *      Flat representation of zipscript state for persistence. Each row corresponds to a single file in a release.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using amFTPd.Core.Zipscript;

namespace amFTPd.Db
{
    /// <summary>
    /// Flat representation of zipscript state for persistence.
    /// Each row corresponds to a single file in a release.
    /// </summary>
    public sealed record ZipscriptFileEntity(
        string ReleasePath,
        string SectionName,
        string FileName,
        long SizeBytes,
        uint? ExpectedCrc,
        uint? ActualCrc,
        ZipscriptFileState State,
        bool IsNuked,
        string? NukeReason,
        string? NukedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastUpdatedAt,
        DateTimeOffset? NukedAt,
        double? NukeMultiplier);
}
