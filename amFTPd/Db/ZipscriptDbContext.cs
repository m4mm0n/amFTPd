/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptDbContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 09:06:52
 *  Last Modified:  2025-12-14 10:56:21
 *  CRC32:          0x319198D5
 *  
 *  Description:
 *      Thin JSON-based storage for zipscript state, using AtomicSnapshot for durability.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using System.Text;
using System.Text.Json;

namespace amFTPd.Db;

/// <summary>
/// Thin JSON-based storage for zipscript state, using AtomicSnapshot for durability.
/// </summary>
public sealed class ZipscriptDbContext
{
    public string DbFilePath { get; }

    public ZipscriptDbContext(string dbFilePath) => DbFilePath = dbFilePath ?? throw new ArgumentNullException(nameof(dbFilePath));

    public (int Version, List<ZipscriptFileEntity> Files) Load()
    {
        if (!File.Exists(DbFilePath))
            return (0, new List<ZipscriptFileEntity>());

        using var stream = File.OpenRead(DbFilePath);
        using var doc = JsonDocument.Parse(stream);

        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("version", out var vProp) &&
            root.TryGetProperty("files", out var filesProp))
        {
            var version = vProp.GetInt32();
            var files = filesProp.Deserialize<List<ZipscriptFileEntity>>() ?? new List<ZipscriptFileEntity>();
            return (version, files);
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            // Legacy v0 format: bare array of entities.
            var files = root.Deserialize<List<ZipscriptFileEntity>>() ?? new List<ZipscriptFileEntity>();
            return (0, files);
        }

        // Corrupt / unknown format.
        return (0, new List<ZipscriptFileEntity>());
    }

    public void Save(int version, IReadOnlyCollection<ZipscriptFileEntity> files)
    {
        var payload = new
        {
            version,
            files
        };

        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = false
            });

        var bytes = Encoding.UTF8.GetBytes(json);
        AtomicSnapshot.WriteAtomic(DbFilePath, bytes);
    }
}