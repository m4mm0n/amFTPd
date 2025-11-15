namespace amFTPd.Config.Daemon;

public sealed record AmFtpdStorageConfig(
    string UsersDbPath,
    string SectionsPath,
    string UserStoreBackend // e.g. "json" (default), "litedb", "custom"
);