namespace amFTPd.Config.Ftpd;

/// <summary>
/// Represents the configuration for a collection of FTP sections.
/// </summary>
/// <remarks>This record encapsulates a list of <see cref="FtpSection"/> objects, which define individual FTP
/// section configurations. It provides a static <see cref="Empty"/> property for scenarios where no sections are
/// required.</remarks>
/// <param name="Sections">The list of <see cref="FtpSection"/> objects that define the FTP sections. Cannot be null.</param>
public sealed record FtpSectionConfig(List<FtpSection> Sections)
{
    /// <summary>
    /// Gets an empty <see cref="FtpSectionConfig"/> instance with no configured sections.
    /// </summary>
    public static FtpSectionConfig Empty => new(new List<FtpSection>());
}