namespace amFTPd.Config.Daemon;

/// <summary>
/// Represents the root configuration for the amFTPd, encompassing server, TLS, and storage settings.
/// </summary>
/// <param name="Server">The configuration settings for the FTP server, including host, port, and other server-specific options.</param>
/// <param name="Tls">The configuration settings for TLS, specifying certificates and security protocols for secure communication.</param>
/// <param name="Storage">The configuration settings for storage, defining paths, quotas, and other storage-related options.</param>
public sealed record AmFtpdConfigRoot(
    AmFtpdServerConfig Server,
    AmFtpdTlsConfig Tls,
    AmFtpdStorageConfig Storage
);