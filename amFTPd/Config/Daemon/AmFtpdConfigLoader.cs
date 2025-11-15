using System.Net;
using System.Text.Json;
using amFTPd.Config.Ftpd;
using amFTPd.Logging;
using amFTPd.Security;

namespace amFTPd.Config.Daemon;

public static class AmFtpdConfigLoader
{
    public static async Task<AmFtpdRuntimeConfig> LoadAsync(
        string configPath,
        IFtpLogger logger)
    {
        if (!File.Exists(configPath))
        {
            logger.Log(FtpLogLevel.Warn, $"Config file '{configPath}' not found, creating default.");

            var defaultRoot = new AmFtpdConfigRoot(
                Server: new AmFtpdServerConfig(
                    BindAddress: "0.0.0.0",
                    Port: 2121,
                    PassivePortStart: 50000,
                    PassivePortEnd: 50100,
                    RootPath: "ftproot",
                    WelcomeMessage: "Welcome to amFTPd",
                    AllowAnonymous: false,
                    RequireTlsForAuth: true,
                    DataChannelProtectionDefault: "P",
                    AllowActiveMode: true,
                    AllowFxp: false
                ),
                Tls: new AmFtpdTlsConfig(
                    PfxPath: "servercert.pfx",
                    PfxPassword: "changeit",
                    SubjectName: "CN=localhost"
                ),
                Storage: new AmFtpdStorageConfig(
                    UsersDbPath: "amftpd-users.json",
                    SectionsPath: "amftpd-sections.json",
                    UserStoreBackend: "json"
                )
            );

            var jsonDefault = JsonSerializer.Serialize(
                defaultRoot,
                new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(configPath, jsonDefault);

            logger.Log(FtpLogLevel.Info, $"Default config written to '{configPath}'.");
        }

        var json = await File.ReadAllTextAsync(configPath);
        var root = JsonSerializer.Deserialize<AmFtpdConfigRoot>(json)
                   ?? throw new InvalidOperationException("Invalid amftpd config file.");

        // --- Build FtpConfig ---
        var bindIp = IPAddress.Parse(root.Server.BindAddress);
        var rootPath = Path.GetFullPath(
            Path.IsPathRooted(root.Server.RootPath)
                ? root.Server.RootPath
                : Path.Combine(AppContext.BaseDirectory, root.Server.RootPath));

        Directory.CreateDirectory(rootPath);

        var ftpCfg = new FtpConfig(
            BindAddress: bindIp,
            Port: root.Server.Port,
            PassivePorts: (root.Server.PassivePortStart, root.Server.PassivePortEnd),
            RootPath: rootPath,
            WelcomeMessage: root.Server.WelcomeMessage,
            AllowAnonymous: root.Server.AllowAnonymous,
            HomeDirs: new Dictionary<string, string>(),
            EnableExplicitTls: true,
            RequireTlsForAuth: root.Server.RequireTlsForAuth,
            DataChannelProtectionDefault: root.Server.DataChannelProtectionDefault,
            AllowActiveMode: root.Server.AllowActiveMode,
            AllowFxp: root.Server.AllowFxp
        );

        // --- User store backend selection ---
        IUserStore userStore = root.Storage.UserStoreBackend.ToLowerInvariant() switch
        {
            "json" => InMemoryUserStore.LoadFromFile(root.Storage.UsersDbPath),

            // Placeholder for future LiteDb/custom backends:
            // "litedb" => new LiteDbUserStore(root.Storage.UsersDbPath, logger),
            // "custom" => new CustomUserStore(...),

            _ => throw new NotSupportedException(
                $"Unknown user store backend '{root.Storage.UserStoreBackend}'.")
        };

        // --- Sections ---
        var sections = SectionManager.LoadFromFile(root.Storage.SectionsPath);

        // --- TLS ---
        var tlsCfg = await TlsConfig.CreateOrLoadAsync(
            pfxPath: root.Tls.PfxPath,
            pfxPassword: root.Tls.PfxPassword,
            subjectName: root.Tls.SubjectName,
            logger: logger);

        return new AmFtpdRuntimeConfig
        {
            FtpConfig = ftpCfg,
            UserStore = userStore,
            Sections = sections,
            TlsConfig = tlsCfg
        };
    }
}