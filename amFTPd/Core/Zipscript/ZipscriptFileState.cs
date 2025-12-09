/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptFileState.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 04:38:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xD72B97DF
 *  
 *  Description:
 *      Represents the state of a file in relation to its SFV (Simple File Verification) entry.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core.Zipscript;

/// <summary>
/// Represents the state of a file in relation to its SFV (Simple File Verification) entry.
/// </summary>
/// <remarks>This enumeration is used to track the status of a file during verification processes, such as
/// whether the file is pending, missing, valid, or has issues like a CRC mismatch.</remarks>
public enum ZipscriptFileState
{
    Pending,   // we saw the file but no SFV entry yet
    Missing,   // SFV entry exists but file not seen (or not uploaded yet)
    Ok,        // CRC matches SFV
    BadCrc,    // CRC mismatch
    Extra      // file exists but not listed in SFV
}