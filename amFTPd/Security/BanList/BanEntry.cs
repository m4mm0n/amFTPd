/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           BanEntry.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-11 03:01:41
 *  Last Modified:  2025-12-11 03:01:41
 *  CRC32:          0xDAC17AD8
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

using System.Net;

namespace amFTPd.Security.BanList;

internal sealed class BanEntry
{
    public IPAddress? Address { get; init; }
    public CidrBlock? Cidr { get; init; }
    public DateTime? ExpiresUtc { get; set; }
    public string? Reason { get; init; }

    public bool IsExpired(DateTime now) =>
        ExpiresUtc.HasValue && ExpiresUtc.Value <= now;

    public bool IsPermanent => !ExpiresUtc.HasValue;
}