/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptDbMigrations.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 09:06:52
 *  Last Modified:  2025-12-14 10:56:32
 *  CRC32:          0x44480BAB
 *  
 *  Description:
 *      Handles schema / format migrations for the zipscript DB.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Db;

/// <summary>
/// Handles schema / format migrations for the zipscript DB.
/// </summary>
public static class ZipscriptDbMigrations
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// Ensures the zipscript DB is in a supported format. Currently this just wraps the
    /// old v0 "bare array" format into a versioned payload, but the method is designed to
    /// be extended when needed.
    /// </summary>
    public static void EnsureSchema(
        ZipscriptDbContext ctx,
        Action<string>? logWarning = null)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        var (version, files) = ctx.Load();

        if (version == CurrentVersion)
            return;

        if (version > CurrentVersion)
        {
            logWarning?.Invoke(
                $"Zipscript DB version {version} is newer than supported version {CurrentVersion}. " +
                "Consider upgrading amFTPd.");
            return;
        }

        // v0 -> v1: just wrap existing rows into versioned structure.
        try
        {
            ctx.Save(CurrentVersion, files);
        }
        catch (Exception ex)
        {
            logWarning?.Invoke($"Failed to migrate zipscript DB to v{CurrentVersion}: {ex.Message}");
        }
    }
}