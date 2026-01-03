using System.Globalization;

namespace amFTPd.Utils;

public static class SfvParser
{
    public static Dictionary<string, uint> Parse(string sfvPath)
    {
        var result = new Dictionary<string, uint>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(sfvPath))
        {
            var l = line.Trim();
            if (l.Length == 0 || l.StartsWith(";"))
                continue;

            var parts = l.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                continue;

            if (uint.TryParse(
                    parts[^1],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var crc))
            {
                var file = string.Join(
                    ' ',
                    parts[..^1]);

                result[file] = crc;
            }
        }

        return result;
    }
}