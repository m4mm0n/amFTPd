using System.Linq;

namespace amFTPd.Core.Import;

/// <summary>
/// Specifies the available import source types for directory listings.
/// </summary>
/// <remarks>Use this enumeration to indicate the format or origin of data being imported. The value determines
/// how the import process interprets and parses the input. Additional values may be added in future versions to support
/// more source types.</remarks>
public enum ImportFlavor
{
    Unknown,
    GlFtpd,
    IoFtpd
}

/// <summary>
/// Shared parser for user-provided flavor tokens.
/// </summary>
/// <remarks>Accepts common legacy aliases such as IOFTPD/GLFTP as well as the enum names.</remarks>
public static class ImportFlavorParser
{
    public static bool TryParse(string? value, out ImportFlavor flavor)
    {
        flavor = ImportFlavor.Unknown;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (Enum.TryParse(value, true, out flavor))
            return true;

        var normalized = new string(value
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToUpperInvariant();

        flavor = normalized switch
        {
            "IO" or "IOFTPD" or "IOFTP" => ImportFlavor.IoFtpd,
            "GL" or "GLFTP" or "GLFTPD" or "GLF" => ImportFlavor.GlFtpd,
            _ => ImportFlavor.Unknown
        };

        return flavor != ImportFlavor.Unknown;
    }
}
