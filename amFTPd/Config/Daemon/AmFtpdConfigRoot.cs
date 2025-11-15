namespace amFTPd.Config.Daemon;

public sealed record AmFtpdConfigRoot(
    AmFtpdServerConfig Server,
    AmFtpdTlsConfig Tls,
    AmFtpdStorageConfig Storage
);