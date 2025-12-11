/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           DbFsck.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 20:18:19
 *  Last Modified:  2025-12-11 08:13:53
 *  CRC32:          0xC170683B
 *  
 *  Description:
 *      Provides functionality to validate the integrity and structure of a database file, including its associated metadata...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Utils;
using System.Security.Cryptography;
using System.Text;

namespace amFTPd.Db
{
    /// <summary>
    /// Provides functionality to validate the integrity and structure of a database file, including its associated
    /// metadata and write-ahead log (WAL) files.
    /// </summary>
    /// <remarks>The <see cref="DbFsck"/> class is designed to perform a series of checks on a database file
    /// to ensure its consistency and correctness. This includes verifying the presence and validity of the salt file,
    /// decrypting and decompressing the database snapshot, and validating the structure of records within the snapshot.
    /// Additionally, it can validate the integrity of the WAL file if present. <para> This class is intended for use in
    /// scenarios where database corruption or misconfiguration is suspected, and detailed diagnostics are required to
    /// identify and resolve issues. </para> <para> The class supports optional debug logging via the <see
    /// cref="DebugLog"/> delegate, which can be used to capture detailed information about the validation process.
    /// </para></remarks>
    public static class DbFsck
    {
        public static Action<string>? DebugLog;

        public static FsckResult CheckDatabase(string dbPath, string masterPassword)
        {
            var result = new FsckResult();
            DebugLog?.Invoke($"[FSCK] Checking {dbPath}");

            // -----------------------------------------------------------------
            // 1) Check SALT FILE
            // -----------------------------------------------------------------
            var saltPath = dbPath + ".salt";

            if (!File.Exists(saltPath))
            {
                result.AddError("Missing .salt file.");
                return result;
            }

            var salt = File.ReadAllBytes(saltPath);
            if (salt.Length != 32)
                result.AddError("Salt file must be 32 bytes.");

            // Derive key for decrypting snapshot/WAL entries
            byte[] masterKey;
            try
            {
                masterKey = DeriveKey(masterPassword, salt);
            }
            catch
            {
                result.AddError("Failed deriving AES key from password.");
                return result;
            }

            // -----------------------------------------------------------------
            // 2) Validate Snapshot file
            // -----------------------------------------------------------------
            if (!File.Exists(dbPath))
            {
                result.AddError("Snapshot .db file is missing.");
                return result;
            }

            var snapshotBuf = File.ReadAllBytes(dbPath);

            byte[] decryptedSnapshot;

            try
            {
                decryptedSnapshot = DecryptAesGcm(snapshotBuf, masterKey);
            }
            catch
            {
                result.AddError("Snapshot AES-GCM decryption failed (wrong password or corruption).");
                return result;
            }

            byte[] decompressedSnapshot;

            try
            {
                decompressedSnapshot = Lz4Codec.Decompress(decryptedSnapshot);
            }
            catch
            {
                result.AddError("Snapshot decompression failed.");
                return result;
            }

            // -----------------------------------------------------------------
            // 3) Validate record layout of snapshot
            // -----------------------------------------------------------------
            ValidateSnapshotLayout(dbPath, decompressedSnapshot, result);

            // -----------------------------------------------------------------
            // 4) Validate WAL
            // -----------------------------------------------------------------
            var walPath = dbPath + ".wal";

            if (File.Exists(walPath))
                ValidateWal(walPath, masterKey, result);
            else
                DebugLog?.Invoke("[FSCK] No WAL file present.");

            return result;
        }


        // =====================================================================
        // VALIDATE SNAPSHOT RECORD LAYOUT
        // =====================================================================

        private static void ValidateSnapshotLayout(string dbPath, byte[] raw, FsckResult result)
        {
            using var ms = new MemoryStream(raw);
            using var br = new BinaryReader(ms);

            // Determine store type from filename
            var isUser = dbPath.Contains("users", StringComparison.OrdinalIgnoreCase);
            var isGroup = dbPath.Contains("groups", StringComparison.OrdinalIgnoreCase);
            var isSection = dbPath.Contains("sections", StringComparison.OrdinalIgnoreCase);

            if (!(isUser || isGroup || isSection))
                result.AddWarning("Unknown DB type — cannot deeply inspect record layout.");

            try
            {
                var count = br.ReadUInt32();

                for (uint i = 0; i < count; i++)
                {
                    var len = br.ReadUInt32();
                    if (len > raw.Length)
                    {
                        result.AddError($"Invalid record length {len} (beyond file bounds).");
                        return;
                    }

                    var recBytes = br.ReadBytes((int)len);
                    if (recBytes.Length != len)
                    {
                        result.AddError("Record does not match declared length.");
                        return;
                    }

                    if (isUser)
                        ValidateUserRecord(recBytes, result);
                    else if (isGroup)
                        ValidateGroupRecord(recBytes, result);
                    else if (isSection)
                        ValidateSectionRecord(recBytes, result);
                }
            }
            catch (Exception ex)
            {
                result.AddError("Snapshot record parse failed: " + ex.Message);
            }
        }


        // =====================================================================
        // USER RECORD CHECK (v1)
        // =====================================================================

        private static void ValidateUserRecord(byte[] rec, FsckResult result)
        {
            try
            {
                using var ms = new MemoryStream(rec);
                using var br = new BinaryReader(ms);

                var type = br.ReadByte();
                if (type != 0)
                {
                    result.AddWarning("Unknown user record type.");
                    return;
                }

                // Lengths
                var nameLen = br.ReadUInt16();
                var passLen = br.ReadUInt16();
                var homeLen = br.ReadUInt16();
                var groupLen = br.ReadUInt16();

                var flags = br.ReadInt32();
                var maxLogins = br.ReadInt32();
                var idleSec = br.ReadInt32();
                var up = br.ReadInt32();
                var down = br.ReadInt32();
                var credits = br.ReadInt64();

                var ipMaskLen = br.ReadUInt16();
                var identLen = br.ReadUInt16();

                // Validate UTF-8 segments
                if (!TryAdvanceUtf8(br, nameLen, result, "UserName")) return;
                if (!TryAdvanceUtf8(br, passLen, result, "PasswordHash")) return;
                if (!TryAdvanceUtf8(br, homeLen, result, "HomeDir")) return;

                if (groupLen > 0 && !TryAdvanceUtf8(br, groupLen, result, "GroupName")) return;
                if (ipMaskLen > 0 && !TryAdvanceUtf8(br, ipMaskLen, result, "AllowedIpMask")) return;
                if (identLen > 0 && !TryAdvanceUtf8(br, identLen, result, "RequiredIdent")) return;

                // Everything parsed correctly
            }
            catch (Exception ex)
            {
                result.AddError("User record corrupted: " + ex.Message);
            }
        }


        // =====================================================================
        // GROUP RECORD CHECK
        // =====================================================================

        private static void ValidateGroupRecord(byte[] rec, FsckResult result)
        {
            try
            {
                using var ms = new MemoryStream(rec);
                using var br = new BinaryReader(ms);

                var nameLen = br.ReadUInt16();
                var descLen = br.ReadUInt16();

                var userCount = br.ReadUInt16();

                var userLens = new ushort[userCount];
                for (var i = 0; i < userCount; i++)
                    userLens[i] = br.ReadUInt16();

                var secCount = br.ReadUInt16();
                var secLens = new ushort[secCount];

                for (var i = 0; i < secCount; i++)
                    secLens[i] = br.ReadUInt16();

                if (!TryAdvanceUtf8(br, nameLen, result, "GroupName")) return;
                if (!TryAdvanceUtf8(br, descLen, result, "Description")) return;

                for (var i = 0; i < userCount; i++)
                    if (!TryAdvanceUtf8(br, userLens[i], result, "UserEntry"))
                        return;

                for (var i = 0; i < secCount; i++)
                {
                    if (!TryAdvanceUtf8(br, secLens[i], result, "SectionName"))
                        return;

                    _ = br.ReadInt64(); // credits
                }
            }
            catch (Exception ex)
            {
                result.AddError("Group record corrupted: " + ex.Message);
            }
        }


        // =====================================================================
        // SECTION RECORD CHECK
        // =====================================================================

        private static void ValidateSectionRecord(byte[] rec, FsckResult result)
        {
            try
            {
                using var ms = new MemoryStream(rec);
                using var br = new BinaryReader(ms);

                var nameLen = br.ReadUInt16();
                var pathLen = br.ReadUInt16();

                var up = br.ReadInt64();
                var down = br.ReadInt64();
                var credits = br.ReadInt64();

                if (!TryAdvanceUtf8(br, nameLen, result, "SectionName")) return;
                if (!TryAdvanceUtf8(br, pathLen, result, "RelativePath")) return;
            }
            catch (Exception ex)
            {
                result.AddError("Section record corrupted: " + ex.Message);
            }
        }


        // =====================================================================
        // WAL VALIDATION
        // =====================================================================

        private static void ValidateWal(string walPath, byte[] masterKey, FsckResult result)
        {
            DebugLog?.Invoke($"[FSCK] Validating WAL {walPath}");

            using var fs = new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var br = new BinaryReader(fs);

            try
            {
                while (fs.Position < fs.Length)
                {
                    if (fs.Length - fs.Position < 4)
                    {
                        result.AddWarning("Partial WAL entry detected at end (incomplete length field).");
                        break;
                    }

                    var len = br.ReadInt32();
                    if (len <= 0 || len > fs.Length)
                    {
                        result.AddError("Invalid WAL entry length.");
                        break;
                    }

                    if (fs.Length - fs.Position < len)
                    {
                        result.AddWarning("Partial WAL entry, truncated mid-file.");
                        break;
                    }

                    var entry = br.ReadBytes(len);

                    WalEntryType type;
                    try
                    {
                        type = PeekWalType(entry, masterKey);
                    }
                    catch
                    {
                        result.AddError("WAL entry decryption or type parsing failed.");
                        continue;
                    }

                    // Basic sanity check
                    if (!Enum.IsDefined(typeof(WalEntryType), type))
                        result.AddWarning($"Unknown WAL entry type: {type}");
                }
            }
            catch (Exception ex)
            {
                result.AddError("WAL parse failed: " + ex.Message);
            }
        }

        private static WalEntryType PeekWalType(byte[] enc, byte[] key)
        {
            // Decrypt but do not decompress fully; only peek first byte of payload
            ReadOnlySpan<byte> nonce = enc[..12];
            ReadOnlySpan<byte> tag = enc[^16..];
            ReadOnlySpan<byte> cipher = enc[12..^16];

            var compressed = new byte[cipher.Length];

            using (var gcm = new AesGcm(key, 16))
                gcm.Decrypt(nonce, cipher, tag, compressed);

            // LZ4 decompress enough to read first byte
            var raw = Lz4Codec.Decompress(compressed);

            return (WalEntryType)raw[0];
        }


        // =====================================================================
        // HELPERS
        // =====================================================================

        private static bool TryAdvanceUtf8(BinaryReader br, int len, FsckResult result, string field)
        {
            try
            {
                var bytes = br.ReadBytes(len);
                if (bytes.Length != len)
                {
                    result.AddError($"{field} truncated before end of record.");
                    return false;
                }

                Encoding.UTF8.GetString(bytes); // throws if invalid
                return true;
            }
            catch
            {
                result.AddError($"{field} invalid UTF-8 or corrupted.");
                return false;
            }
        }

        private static byte[] DecryptAesGcm(byte[] buf, byte[] key)
        {
            ReadOnlySpan<byte> nonce = buf[..12];
            ReadOnlySpan<byte> tag = buf[^16..];
            ReadOnlySpan<byte> cipher = buf[12..^16];

            var plain = new byte[cipher.Length];

            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);

            return plain;
        }

        private static byte[] DeriveKey(string pw, byte[] salt)
        {
            using var pbk = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(pw),
                salt,
                200_000,
                HashAlgorithmName.SHA256);

            return pbk.GetBytes(32);
        }
    }
}
