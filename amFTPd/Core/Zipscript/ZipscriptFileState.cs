/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptFileState.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 04:38:40
 *  Last Modified:  2025-12-14 10:53:51
 *  CRC32:          0x4F5067BA
 *  
 *  Description:
 *      Represents the state of a file in relation to its SFV entry and lifecycle.
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
/// Represents the state of a file in relation to its SFV entry and lifecycle.
/// </summary>
public enum ZipscriptFileState
{
    /// <summary>We saw the file but have no SFV entry yet.</summary>
    Pending,

    /// <summary>SFV entry exists but file not seen (or not uploaded yet).</summary>
    Missing,

    /// <summary>CRC matches SFV.</summary>
    Ok,

    /// <summary>CRC mismatch.</summary>
    BadCrc,

    /// <summary>File exists but is not listed in SFV.</summary>
    Extra,

    /// <summary>File has been deleted from disk.</summary>
    Deleted,

    /// <summary>File is part of a nuked release.</summary>
    Nuked
}