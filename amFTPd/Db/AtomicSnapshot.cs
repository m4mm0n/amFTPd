/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AtomicSnapshot.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 19:57:08
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x4B7745D4
 *  
 *  Description:
 *      Writes the specified byte array to a file in an atomic manner, ensuring that the file is either fully written or not...
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
/// Writes the specified byte array to a file in an atomic manner, ensuring that the file is either fully written or
/// not modified at all.
/// </summary>
/// <remarks>This method ensures atomicity by writing the data to a temporary file first, flushing it to
/// disk, and then replacing the target file in a series of safe, controlled steps. If the process is interrupted,
/// the target file remains unmodified.</remarks>
public static class AtomicSnapshot
{
    /// <summary>
    /// Writes the specified data to a file at the given path in an atomic manner, ensuring that the file is not
    /// left in a partially written state.
    /// </summary>
    /// <remarks>This method ensures atomicity by writing the data to a temporary file first, flushing
    /// it to disk, and then renaming it to the target file. If the target file already exists, it will be replaced
    /// atomically. This approach minimizes the risk of data corruption in case of interruptions such as application
    /// crashes or power failures during the write operation.</remarks>
    /// <param name="targetPath">The full path of the target file to write to. This path must be writable and accessible.</param>
    /// <param name="data">The byte array containing the data to be written to the file.</param>
    public static void WriteAtomic(string targetPath, byte[] data)
    {
        var tmp = targetPath + ".tmp";
        var atomic = targetPath + ".atomic";

        // 1. Write temporary file
        File.WriteAllBytes(tmp, data);

        // 2. Flush to disk
        using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
            fs.Flush(true);

        // 3. Rename to atomic target
        if (File.Exists(atomic))
            File.Delete(atomic);

        File.Move(tmp, atomic);

        // 4. Replace snapshot atomically
        if (File.Exists(targetPath))
            File.Delete(targetPath);

        File.Move(atomic, targetPath);
    }
}