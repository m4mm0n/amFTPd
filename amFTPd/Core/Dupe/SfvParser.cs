using System.Globalization;

namespace amFTPd.Core.Dupe;

/// <summary>
/// Provides methods for parsing SFV (Simple File Verification) files and extracting file names and their associated
/// CRC32 checksums.
/// </summary>
/// <remarks>The SfvParser class is intended for reading and interpreting SFV files, which are commonly used to
/// verify the integrity of files using CRC32 checksums. All members of this class are static and thread safe.</remarks>
public static class SfvParser
{
    public static IReadOnlyDictionary<string, uint> Parse(string path)
    {
        var dict = new Dictionary<string, uint>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(path))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith(";"))
                continue;

            var parts = t.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                continue;

            if (uint.TryParse(
                    parts[^1],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var crc))
            {
                dict[parts[0]] = crc;
            }
        }

        return dict;
    }
}