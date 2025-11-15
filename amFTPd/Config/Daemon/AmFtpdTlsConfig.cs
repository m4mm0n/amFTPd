namespace amFTPd.Config.Daemon;

public sealed record AmFtpdTlsConfig(
    string PfxPath,
    string PfxPassword,
    string SubjectName
);