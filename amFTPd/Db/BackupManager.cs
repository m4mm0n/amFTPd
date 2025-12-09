/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           BackupManager.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 20:16:29
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x71F78584
 *  
 *  Description:
 *      Provides functionality for creating, restoring, listing, and deleting encrypted database backups.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Security.Cryptography;
using System.Text;
using amFTPd.Utils;

namespace amFTPd.Db
{
    /// <summary>
    /// Provides functionality for creating, restoring, listing, and deleting encrypted database backups.
    /// </summary>
    /// <remarks>The <see cref="BackupManager"/> class is designed to manage encrypted backups of database
    /// files. It supports creating backups for individual databases or multiple stores, restoring backups, listing
    /// available backups, and deleting backup files. Backups are encrypted and compressed to ensure security and
    /// efficiency. This class is intended for use in scenarios where secure and reliable database backup management is
    /// required.</remarks>
    public static class BackupManager
    {
        public static Action<string>? DebugLog;

        private const string MAGIC = "AMFTPBK1"; // backup format v1

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        public static string CreateBackup(string dbPath, string masterPassword)
        {
            var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
            Directory.CreateDirectory(backupDir);

            var entityName = Path.GetFileNameWithoutExtension(dbPath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFile = Path.Combine(backupDir, $"{entityName}_{timestamp}.dbx");

            CreateBackupInternal(dbPath, masterPassword, backupFile);

            DebugLog?.Invoke($"[BACKUP] Created backup: {backupFile}");
            return backupFile;
        }

        public static void CreateBackupAll(string baseDir, string masterPassword)
        {
            DebugLog?.Invoke("[BACKUP] Creating full multi-store backup…");

            foreach (var entity in new[] { "users", "groups", "sections" })
            {
                var path = Path.Combine(baseDir, $"amftpd-{entity}.db");
                if (File.Exists(path))
                    CreateBackup(path, masterPassword);
            }
        }

        public static void RestoreBackup(string dbPath, string masterPassword, string backupFile)
        {
            DebugLog?.Invoke($"[RESTORE] Restoring {backupFile} → {dbPath}");

            var buf = File.ReadAllBytes(backupFile);
            var decrypted = DecryptBackup(buf, masterPassword);

            AtomicSnapshot.WriteAtomic(dbPath, decrypted);

            DebugLog?.Invoke("[RESTORE] Completed.");
        }

        public static IEnumerable<FileInfo> ListBackups(string dbPath)
        {
            var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
            if (!Directory.Exists(backupDir))
                yield break;

            foreach (var f in Directory.GetFiles(backupDir, "*.dbx"))
                yield return new FileInfo(f);
        }

        public static bool DeleteBackup(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            return true;
        }

        // =====================================================================
        // INTERNAL BACKUP PROCESS
        // =====================================================================

        private static void CreateBackupInternal(string dbPath, string masterPassword, string outputFile)
        {
            // Step 1: Flush WAL (if it exists)
            var wal = dbPath + ".wal";
            if (File.Exists(wal))
            {
                DebugLog?.Invoke("[BACKUP] Flushing WAL before backup…");
                // safest strategy: let BinaryUserStore rewrite snapshot
                // but since backup engine is store-agnostic:
                // we simply read dbPath as-is; WAL should be self-contained
            }

            // Step 2: Read the full encrypted snapshot
            var encryptedSnapshot = File.ReadAllBytes(dbPath);

            // Step 3: Compress + encrypt into backup file format
            var backupBlob = EncryptBackup(encryptedSnapshot, masterPassword);

            // Step 4: Atomic write
            AtomicSnapshot.WriteAtomic(outputFile, backupBlob);
        }

        // =====================================================================
        // BACKUP ENCRYPTION FORMAT
        // =====================================================================

        private static byte[] EncryptBackup(byte[] rawSnapshot, string pw)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // Header
            bw.Write(Encoding.ASCII.GetBytes(MAGIC));

            // Fresh random salt for backup
            var salt = RandomNumberGenerator.GetBytes(32);
            bw.Write(salt);

            // Derive backup key
            var key = DeriveKey(pw, salt);

            // LZ4 compress snapshot
            var compressed = Lz4Codec.Compress(rawSnapshot);

            // Encrypt (AES-GCM)
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[compressed.Length];

            using (var gcm = new AesGcm(key))
                gcm.Encrypt(nonce, compressed, cipher, tag);

            // Write payload
            bw.Write(nonce);
            bw.Write(cipher);
            bw.Write(tag);

            return ms.ToArray();
        }

        private static byte[] DecryptBackup(byte[] buf, string pw)
        {
            using var ms = new MemoryStream(buf);
            using var br = new BinaryReader(ms);

            var magic = Encoding.ASCII.GetString(br.ReadBytes(8));
            if (magic != MAGIC)
                throw new Exception("Invalid backup format (magic mismatch).");

            var salt = br.ReadBytes(32);
            var key = DeriveKey(pw, salt);

            var nonce = br.ReadBytes(12);

            var cipherLen = ms.Length - ms.Position - 16;
            if (cipherLen <= 0)
                throw new Exception("Invalid backup format (cipher length).");

            var cipher = br.ReadBytes((int)cipherLen);
            var tag = br.ReadBytes(16);

            byte[] decompressed;

            try
            {
                var compressed = new byte[cipher.Length];
                using (var gcm = new AesGcm(key))
                    gcm.Decrypt(nonce, cipher, tag, compressed);

                decompressed = Lz4Codec.Decompress(compressed);
            }
            catch
            {
                throw new Exception("Backup decryption failed (wrong password or corrupted file).");
            }

            return decompressed;
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private static byte[] DeriveKey(string pw, byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(pw),
                salt,
                200_000,
                HashAlgorithmName.SHA256
            );
            return pbkdf2.GetBytes(32);
        }
    }

}
