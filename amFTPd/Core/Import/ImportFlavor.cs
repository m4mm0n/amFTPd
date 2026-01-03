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