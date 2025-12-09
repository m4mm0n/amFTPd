/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FsckResult.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 20:20:07
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xF36A38B7
 *  
 *  Description:
 *      Represents the result of a file system consistency check (FSCK), including any errors or warnings encountered during...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Db;

/// <summary>
/// Represents the result of a file system consistency check (FSCK), including any errors or warnings encountered
/// during the process.
/// </summary>
/// <remarks>This class provides a structured way to collect and analyze the results of an FSCK operation.
/// Errors and warnings are stored in separate lists, and the overall success of the operation can be determined  by
/// checking the <see cref="Success"/> property.</remarks>
public sealed class FsckResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool Success => Errors.Count == 0;

    public void AddError(string msg)
    {
        Errors.Add(msg);
        DbFsck.DebugLog?.Invoke("[FSCK] ERROR: " + msg);
    }

    public void AddWarning(string msg)
    {
        Warnings.Add(msg);
        DbFsck.DebugLog?.Invoke("[FSCK] WARN: " + msg);
    }
}