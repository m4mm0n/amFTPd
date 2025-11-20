/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using System.Security.Cryptography;
using amFTPd.Utils;

namespace amFTPd.Db;

/// <summary>
/// Represents a Write-Ahead Log (WAL) file that supports appending, reading, and managing log entries with
/// encryption and automatic compaction based on size limits.
/// </summary>
/// <remarks>The <see cref="WalFile"/> class provides functionality for securely storing and retrieving
/// log entries in a write-ahead log format. Log entries are encrypted using AES-GCM and compressed to optimize
/// storage. The class also supports automatic compaction by clearing the WAL file when its size exceeds the
/// configured limit.</remarks>
public sealed class WalFile
{
    private readonly string _walPath;
    private readonly byte[] _masterKey;

    /// <summary>
    /// Gets or sets the maximum size, in bytes, of the Write-Ahead Log (WAL) before it is truncated.
    /// </summary>
    /// <remarks>This property determines the upper limit on the size of the WAL. When the size
    /// exceeds this value,  the log is truncated to maintain the specified limit. Adjust this value based on the
    /// application's  logging requirements and available storage capacity.</remarks>
    public long MaxWalSizeBytes { get; set; } = 5 * 1024 * 1024;
    /// <summary>
    /// Initializes a new instance of the <see cref="WalFile"/> class with the specified WAL file path and master
    /// key.
    /// </summary>
    /// <param name="walPath">The file path to the Write-Ahead Log (WAL) file. Cannot be null or empty.</param>
    /// <param name="masterKey">The master key used for encryption or decryption. Cannot be null.</param>
    public WalFile(string walPath, byte[] masterKey)
    {
        _walPath = walPath;
        _masterKey = masterKey;
    }
    /// <summary>
    /// Appends a write-ahead log (WAL) entry to the log file.
    /// </summary>
    /// <remarks>The method writes the encrypted entry to the log file, preceded by its length in
    /// bytes.  The log file is flushed to ensure the data is persisted immediately.</remarks>
    /// <param name="entry">The <see cref="WalEntry"/> to append to the log file. This entry will be encrypted before being written.</param>
    public void Append(WalEntry entry)
    {
        using var fs = new FileStream(_walPath, FileMode.Append, FileAccess.Write, FileShare.Read);

        var encrypted = EncryptEntry(entry);
        fs.Write(BitConverter.GetBytes(encrypted.Length));
        fs.Write(encrypted);
        fs.Flush(true);
    }
    /// <summary>
    /// Reads all valid write-ahead log (WAL) entries from the specified log file.
    /// </summary>
    /// <remarks>This method reads entries sequentially from the log file located at the path
    /// specified by the <c>_walPath</c> field. If the file does not exist, the method returns an empty sequence.
    /// Entries are decrypted and validated before being returned. Corrupted or incomplete entries are skipped, and
    /// the enumeration stops if the file is found to be in an inconsistent state.</remarks>
    /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="WalEntry"/> objects representing the valid entries in the log
    /// file. The sequence will be empty if the log file does not exist or contains no valid entries.</returns>
    public IEnumerable<WalEntry> ReadAll()
    {
        if (!File.Exists(_walPath))
            yield break;

        using var fs = new FileStream(_walPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var lenBuf = new byte[4];

        while (fs.Position < fs.Length)
        {
            if (fs.Read(lenBuf, 0, 4) != 4)
                yield break;

            var len = BitConverter.ToInt32(lenBuf, 0);
            if (len <= 0 || fs.Position + len > fs.Length)
                yield break; // corrupted or partial write

            var encBuf = new byte[len];
            fs.Read(encBuf, 0, len);

            var e = DecryptEntry(encBuf);
            if (e != null)
                yield return e;
        }
    }
    /// <summary>
    /// Deletes the Write-Ahead Log (WAL) file if it exists.
    /// </summary>
    /// <remarks>This method checks for the existence of the WAL file at the specified path and
    /// deletes it if found.  Use this method to clear the WAL file and reset the state for subsequent
    /// operations.</remarks>
    public void Clear()
    {
        if (File.Exists(_walPath))
            File.Delete(_walPath);
    }
    /// <summary>
    /// Determines whether the Write-Ahead Log (WAL) file exceeds the maximum allowed size and requires compaction.
    /// </summary>
    /// <remarks>This method checks the existence and size of the WAL file specified by the internal
    /// path. If the file does not exist, compaction is not required.</remarks>
    /// <returns><see langword="true"/> if the WAL file exists and its size exceeds the value of <see
    /// cref="MaxWalSizeBytes"/>; otherwise, <see langword="false"/>.</returns>
    public bool NeedsCompaction()
    {
        if (!File.Exists(_walPath))
            return false;

        var size = new FileInfo(_walPath).Length;
        return size > MaxWalSizeBytes;
    }

    // =========================================================
    // Encryption / Decryption
    // =========================================================
    private byte[] EncryptEntry(WalEntry entry)
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)entry.Type);
        ms.Write(entry.Payload);
        var raw = ms.ToArray();

        var compressed = Lz4Codec.Compress(raw);

        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[compressed.Length];

        using (var gcm = new AesGcm(_masterKey))
            gcm.Encrypt(nonce, compressed, ciphertext, tag);

        using var outMs = new MemoryStream();
        outMs.Write(nonce);
        outMs.Write(ciphertext);
        outMs.Write(tag);
        return outMs.ToArray();
    }
    private WalEntry? DecryptEntry(byte[] encrypted)
    {
        try
        {
            ReadOnlySpan<byte> nonce = encrypted[..12];
            ReadOnlySpan<byte> tag = encrypted[^16..];
            ReadOnlySpan<byte> ciphertext = encrypted[12..^16];

            var decompressed = new byte[ciphertext.Length];

            using (var gcm = new AesGcm(_masterKey))
                gcm.Decrypt(nonce, ciphertext, tag, decompressed);

            var raw = Lz4Codec.Decompress(decompressed);

            var type = (WalEntryType)raw[0];
            var payload = new byte[raw.Length - 1];
            Buffer.BlockCopy(raw, 1, payload, 0, payload.Length);

            return new WalEntry(type, payload);
        }
        catch
        {
            // corrupted entry — ignore
            return null;
        }
    }
}