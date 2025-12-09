/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           WalEntry.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 19:53:01
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x25FFF506
 *  
 *  Description:
 *      Represents a write-ahead log (WAL) entry containing a specific type and associated binary payload.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Db
{
    /// <summary>
    /// Represents a write-ahead log (WAL) entry containing a specific type and associated binary payload.
    /// </summary>
    /// <param name="Type">The type of the WAL entry, indicating its purpose or category.</param>
    /// <param name="Payload">The binary data associated with the WAL entry. This payload is identical in structure to a snapshot record.</param>
    public sealed record WalEntry(
        WalEntryType Type,
        byte[] Payload // binary record identical to snapshot record
    );
}
