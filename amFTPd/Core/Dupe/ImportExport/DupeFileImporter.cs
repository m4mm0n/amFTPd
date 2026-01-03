using System.Text;

namespace amFTPd.Core.Dupe.ImportExport;

/// <summary>
/// Provides functionality to import scene dupe entries from a dupefile.dat file.
/// </summary>
/// <remarks>This class cannot be instantiated. All members are static.</remarks>
public static class DupeFileImporter
{
    public static IReadOnlyList<SceneDupeEntry> Import(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.UTF8);

        var magic = br.ReadString();
        if (magic != "AMDP")
            throw new InvalidDataException("Not a dupefile.dat");

        var version = br.ReadByte();
        if (version != 1)
            throw new InvalidDataException($"Unsupported dupefile version {version}");

        var count = br.ReadInt32();
        var list = new List<SceneDupeEntry>(count);

        for (var i = 0; i < count; i++)
        {
            list.Add(new SceneDupeEntry
            {
                Section = br.ReadString(),
                ReleaseName = br.ReadString(),
                Group = br.ReadString(),

                ReleaseDate = DateTimeOffset.FromUnixTimeSeconds(br.ReadInt64()),
                TotalBytes = br.ReadInt64(),
                FileCount = br.ReadInt32(),

                IsNuked = br.ReadBoolean(),
                NukeReason = br.ReadString(),
                NukeMultiplier = br.ReadDouble()
            });
        }

        return list;
    }
}