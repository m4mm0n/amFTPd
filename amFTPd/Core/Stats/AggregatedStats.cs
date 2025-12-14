/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AggregatedStats.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 16:55:54
 *  Last Modified:  2025-12-14 16:58:08
 *  CRC32:          0xABB29E0E
 *  
 *  Description:
 *      Aggregated statistics over a time window based on <see cref="SessionLogEntry"/>.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using amFTPd.Db;

namespace amFTPd.Core.Stats
{
    /// <summary>
    /// Aggregated statistics over a time window based on <see cref="SessionLogEntry"/>.
    /// </summary>
    public sealed record AggregatedStats
    {
        public DateTimeOffset FromUtc { get; init; }
        public DateTimeOffset ToUtc { get; init; }

        public long Uploads { get; init; }
        public long Downloads { get; init; }
        public long BytesUploaded { get; init; }
        public long BytesDownloaded { get; init; }

        public long Nukes { get; init; }
        public long UnNukes { get; init; }
        public long Pres { get; init; }
        public long Deletes { get; init; }

        public long Logins { get; init; }
        public long UniqueUsers { get; init; }
    }
}
