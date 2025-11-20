/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using amFTPd.Config.Ftpd;
using amFTPd.Logging;
using amFTPd.Security;
using System.Net;
using System.Text.Json;
using amFTPd.Db;

namespace amFTPd.Config.Daemon;

/// <summary>
/// Provides functionality to load and initialize the runtime configuration for the amFTPd server.
/// </summary>
/// <remarks>This class is responsible for loading the configuration from a specified file path. If the
/// configuration file does not exist, it creates a default configuration file with predefined settings. The loaded
/// configuration includes server settings, TLS configuration, user store backend, and section management.</remarks>
public static class AmFtpdConfigLoader
{
    /// <summary>
    /// Asynchronously loads the FTP server runtime configuration from the specified file path. If the configuration
    /// file does not exist, a default configuration is created, saved to the file, and returned.
    /// </summary>
    /// <remarks>This method performs the following actions: <list type="bullet"> <item>If the configuration
    /// file does not exist, a default configuration is created, serialized to JSON, and written to the specified
    /// path.</item> <item>The configuration file is then read and deserialized into an <see
    /// cref="AmFtpdRuntimeConfig"/> object.</item> <item>Additional resources, such as the user store, sections, and
    /// TLS configuration, are initialized based on the loaded configuration.</item> </list> The method ensures that the
    /// root path specified in the configuration exists by creating the directory if necessary.</remarks>
    /// <param name="configPath">The path to the configuration file. If the file does not exist, a default configuration is created at this
    /// location.</param>
    /// <param name="logger">An instance of <see cref="IFtpLogger"/> used to log messages during the loading process.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains an <see cref="AmFtpdRuntimeConfig"/>
    /// object representing the loaded or default runtime configuration.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the configuration file contains invalid data or if required fields (e.g., MasterPassword for binary
    /// user store) are not set.</exception>
    /// <exception cref="NotSupportedException">Thrown if the user store backend specified in the configuration is not supported.</exception>
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
                    UsersDbPath: "amftpd-users.db",
                    SectionsPath: "amftpd-sections.json",
                    UserStoreBackend: "binary",
                    MasterPassword: "CHANGE_ME"
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
            HomeDirs: new System.Collections.Generic.Dictionary<string, string>(),
            EnableExplicitTls: true,
            RequireTlsForAuth: root.Server.RequireTlsForAuth,
            DataChannelProtectionDefault: root.Server.DataChannelProtectionDefault,
            AllowActiveMode: root.Server.AllowActiveMode,
            AllowFxp: root.Server.AllowFxp
        );

        // --- User store backend selection (NO switch-expression) ---
        var backend = (root.Storage.UserStoreBackend ?? "json").ToLowerInvariant();
        IUserStore userStore;

        if (backend == "json")
        {
            userStore = InMemoryUserStore.LoadFromFile(root.Storage.UsersDbPath);
        }
        else if (backend == "binary")
        {
            if (string.IsNullOrWhiteSpace(root.Storage.MasterPassword))
                throw new InvalidOperationException("MasterPassword must be set for binary user store.");

            userStore = new BinaryUserStore(root.Storage.UsersDbPath, root.Storage.MasterPassword);
        }
        else
        {
            throw new NotSupportedException($"Unknown user store backend '{backend}'.");
        }

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