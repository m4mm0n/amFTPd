namespace amFTPd.Config.Daemon;

/// <summary>
/// Represents the configuration settings for TLS (Transport Layer Security) in an FTP server.
/// </summary>
/// <param name="PfxPath">The file path to the PFX certificate used for TLS encryption. This must be a valid path to a PFX file.</param>
/// <param name="PfxPassword">The password required to access the PFX certificate. This value cannot be null or empty.</param>
/// <param name="SubjectName">The subject name of the certificate to be used for TLS. This is typically the distinguished name (DN) of the
/// certificate.</param>
public sealed record AmFtpdTlsConfig(
    string PfxPath,
    string PfxPassword,
    string SubjectName
);