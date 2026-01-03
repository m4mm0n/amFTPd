using System.Text;

namespace amFTPd.Core.Dupe.ImportExport;

/// <summary>
/// Provides functionality to export a collection of duplicate scene entries to a binary file in a specific format.
/// </summary>
/// <remarks>The exported file uses a custom binary format with a fixed header and version. This class is intended
/// for scenarios where duplicate scene entry data needs to be persisted or shared in a compact, machine-readable form.
/// The class is static and cannot be instantiated.</remarks>
public static class DupeFileExporter
{
    public static void Export(
        IReadOnlyCollection<SceneDupeEntry> entries,
        string path)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.UTF8);

        bw.Write("AMDP"); // magic
        bw.Write((byte)1); // version
        bw.Write(entries.Count);

        foreach (var e in entries)
        {
            bw.Write(e.Section);
            bw.Write(e.ReleaseName);
            bw.Write(e.Group);

            bw.Write(e.ReleaseDate.ToUnixTimeSeconds());
            bw.Write(e.TotalBytes);
            bw.Write(e.FileCount);

            bw.Write(e.IsNuked);
            bw.Write(e.NukeReason ?? "");
            bw.Write(e.NukeMultiplier);
        }
    }
}