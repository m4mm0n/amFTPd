using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using amFTPd.Config.Daemon;
using amFTPd.Core;
using amFTPd.Db;
using amFTPd.Logging;
using FluentFTP;

namespace amFTPd.Tests;

public class FtpTestFixture : IAsyncLifetime
{
    private FtpServer? _server;
    private QuickLogFtpLogger? _logger;
    private string _tempDir = "";
    public int Port { get; private set; }
    public int PassivePortStart { get; private set; }
    public int PassivePortEnd { get; private set; }
    public AmFtpdRuntimeConfig Runtime { get; private set; } = null!;
    public string RootPath => Path.Combine(_tempDir, "site-root");
    public string ConfigPath => Path.Combine(_tempDir, "amftpd.json");

    // GAdmin credentials
    public string GAdminUser => "admin";
    public string GAdminPass => "adminpass";

    // Normal user credentials
    public string NormalUser => "testuser";
    public string NormalPass => "testpass";

    public IReadOnlyList<FtpTestAccount> RaceAccounts { get; } =
    [
        new("race01", "racepass1"),
        new("race02", "racepass2"),
        new("race03", "racepass3"),
        new("race04", "racepass4"),
        new("race05", "racepass5")
    ];

    public IReadOnlyList<TreasureCoveAccount> TreasureCoveAccounts { get; } =
    [
        new("razor_pre", "prepass1", "Razor1911", "TCV0DAY"),
        new("fairlight_pre", "prepass2", "FairLight", "TCVGAMES"),
        new("class_pre", "prepass3", "CLASS", "TCVAPPS"),
        new("hoodlum_pre", "prepass4", "HOODLUM", "TCVMP3"),
        new("deviance_pre", "prepass5", "DEViANCE", "TCVTV")
    ];

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "amftpd-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        // Create site-root
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(Path.Combine(RootPath, "0DAY"));
        Directory.CreateDirectory(Path.Combine(RootPath, "MP3"));
        Directory.CreateDirectory(Path.Combine(RootPath, "XVID"));
        Directory.CreateDirectory(Path.Combine(RootPath, "STAFF"));
        Directory.CreateDirectory(Path.Combine(RootPath, "SAFE"));
        Directory.CreateDirectory(Path.Combine(RootPath, "TCV0DAY"));
        Directory.CreateDirectory(Path.Combine(RootPath, "TCVGAMES"));
        Directory.CreateDirectory(Path.Combine(RootPath, "TCVAPPS"));
        Directory.CreateDirectory(Path.Combine(RootPath, "TCVMP3"));
        Directory.CreateDirectory(Path.Combine(RootPath, "TCVTV"));

        // Generate random port
        Port = 21000 + Random.Shared.Next(0, 10000);
        PassivePortStart = 30000 + Random.Shared.Next(0, 10000);
        PassivePortEnd = PassivePortStart + 99;

        // Generate config files
        await GenerateConfigFiles();

        // Start Server
        var logger = QuickLogFactory.CreateTestLogger(out _);
        _logger = logger;
        var runtime = await AmFtpdConfigLoader.LoadAsync(ConfigPath, logger);
        Runtime = runtime;

        // Seed users manually since it's a binary/json store
        SeedUsers(runtime);

        _server = new FtpServer(runtime, logger);
        _ = _server.StartAsync();

        // Wait for server to bind
        await Task.Delay(1000);
    }

    private void SeedUsers(AmFtpdRuntimeConfig runtime)
    {
        var userStore = runtime.UserStore;

        // Add Admin (GAdmin / Siteop)
        var admin = new amFTPd.Config.Ftpd.FtpUser
        {
            UserName = GAdminUser,
            PasswordHash = amFTPd.Security.PasswordHasher.HashPassword(GAdminPass),
            GroupName = "admins",
            IsSiteop = true,
            IsAdmin = true,
            MaxCommandsPerMinuteOverride = 10_000
        };
        if (!userStore.TryAddUser(admin, out _))
            userStore.TryUpdateUser(admin, out _);

        // Add Normal User
        var normal = new amFTPd.Config.Ftpd.FtpUser
        {
            UserName = NormalUser,
            PasswordHash = amFTPd.Security.PasswordHasher.HashPassword(NormalPass),
            GroupName = "users",
            CreditsKb = 10_000,
            MaxCommandsPerMinuteOverride = 10_000
        };
        if (!userStore.TryAddUser(normal, out _))
            userStore.TryUpdateUser(normal, out _);

        foreach (var account in RaceAccounts)
        {
            var user = new amFTPd.Config.Ftpd.FtpUser
            {
                UserName = account.UserName,
                PasswordHash = amFTPd.Security.PasswordHasher.HashPassword(account.Password),
                GroupName = "users",
                CreditsKb = 10_000,
                MaxCommandsPerMinuteOverride = 10_000
            };

            if (!userStore.TryAddUser(user, out _))
                userStore.TryUpdateUser(user, out _);
        }

        foreach (var account in TreasureCoveAccounts)
        {
            var user = new amFTPd.Config.Ftpd.FtpUser
            {
                UserName = account.UserName,
                PasswordHash = amFTPd.Security.PasswordHasher.HashPassword(account.Password),
                GroupName = account.GroupName,
                CreditsKb = 25_000,
                MaxCommandsPerMinuteOverride = 10_000
            };

            if (!userStore.TryAddUser(user, out _))
                userStore.TryUpdateUser(user, out _);
        }
    }

    private async Task GenerateConfigFiles()
    {
        var configDir = Path.Combine(_tempDir, "config");
        var dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(Path.Combine(configDir, "rules"));

        // 1. Write Sections.json
        var sectionsPath = Path.Combine(configDir, "sections.json");
        await File.WriteAllTextAsync(sectionsPath, """
        [
          { "Name": "DEFAULT", "RatioRuleName": "STANDARD", "DirectoryRules": [] },
          { "Name": "0DAY", "RatioRuleName": "STANDARD", "DirectoryRules": [] },
          { "Name": "MP3", "RatioRuleName": "STANDARD", "DirectoryRules": [] },
          { "Name": "XVID", "RatioRuleName": "STANDARD", "DirectoryRules": [] },
          { "Name": "STAFF", "VirtualRoot": "/STAFF", "RatioSection": "STANDARD", "RequirePreApproval": true, "AllowedPreGroups": [ "users" ] },
          { "Name": "SAFE", "VirtualRoot": "/SAFE", "RatioSection": "STANDARD", "RequireResumeIntegrity": true },
          { "Name": "TCV0DAY", "VirtualRoot": "/TCV0DAY", "RatioSection": "STANDARD", "RequirePreApproval": true, "AllowedPreGroups": [ "Razor1911" ] },
          { "Name": "TCVGAMES", "VirtualRoot": "/TCVGAMES", "RatioSection": "STANDARD", "RequirePreApproval": true, "AllowedPreGroups": [ "FairLight" ] },
          { "Name": "TCVAPPS", "VirtualRoot": "/TCVAPPS", "RatioSection": "STANDARD", "RequirePreApproval": true, "AllowedPreGroups": [ "CLASS" ] },
          { "Name": "TCVMP3", "VirtualRoot": "/TCVMP3", "RatioSection": "STANDARD", "RequirePreApproval": true, "AllowedPreGroups": [ "HOODLUM" ] },
          { "Name": "TCVTV", "VirtualRoot": "/TCVTV", "RatioSection": "STANDARD", "RequirePreApproval": true, "AllowedPreGroups": [ "DEViANCE" ] }
        ]
        """);

        // 2. Write amftpd.json
        var config = new
        {
            Server = new
            {
                BindAddress = "127.0.0.1",
                Port = Port,
                PassivePortStart = PassivePortStart,
                PassivePortEnd = PassivePortEnd,
                RootPath = RootPath,
                WelcomeMessage = "Welcome to amFTPd Test Server",
                AllowAnonymous = false,
                RequireTlsForAuth = false,
                DataChannelProtectionDefault = "C",
                AllowActiveMode = true,
                AllowFxp = false,
                MaxCommandsPerMinute = 10_000
            },
            Tls = new
            {
                PfxPath = Path.Combine(configDir, "cert.pfx"),
                PfxPassword = "password",
                SubjectName = "CN=amFTPd"
            },
            Ident = new
            {
                Enabled = false,
                Required = false,
                TimeoutMs = 10
            },
            Storage = new
            {
                BaseDirectory = _tempDir,
                MasterPassword = "test-master-password",
                UsersDbPath = Path.Combine(dataDir, "users.json"),
                GroupsDbPath = Path.Combine(dataDir, "groups.db"),
                SectionsDbPath = Path.Combine(dataDir, "sections.db"),
                DupeStoreBackend = "binary",
                DupeStorePath = Path.Combine(dataDir, "dupes"),
                DupeContentDbPath = Path.Combine(dataDir, "dupe-contents.db"),
                UserStoreBackend = "json",
                SectionsPath = sectionsPath,
                RulesPath = Path.Combine(configDir, "rules"),
                ScriptsConfigPath = Path.Combine(configDir, "scripts.json")
            },
            Vfs = new
            {
                Provider = "physical",
                Mounts = new[]
                {
                    new { VirtualRoot = "/", PhysicalPath = RootPath, ReadOnly = false }
                }
            },
            Sections = new Dictionary<string, object>
            {
                { "DEFAULT", new { SectionName = "DEFAULT", RatioRuleName = "STANDARD" } },
                { "0DAY", new { SectionName = "0DAY", RatioRuleName = "STANDARD" } },
                { "MP3", new { SectionName = "MP3", RatioRuleName = "STANDARD" } },
                { "XVID", new { SectionName = "XVID", RatioRuleName = "STANDARD" } },
                { "STAFF", new { SectionName = "STAFF", RatioRuleName = "STANDARD" } },
                { "SAFE", new { SectionName = "SAFE", RatioRuleName = "STANDARD" } },
                { "TCV0DAY", new { SectionName = "TCV0DAY", RatioRuleName = "STANDARD" } },
                { "TCVGAMES", new { SectionName = "TCVGAMES", RatioRuleName = "STANDARD" } },
                { "TCVAPPS", new { SectionName = "TCVAPPS", RatioRuleName = "STANDARD" } },
                { "TCVMP3", new { SectionName = "TCVMP3", RatioRuleName = "STANDARD" } },
                { "TCVTV", new { SectionName = "TCVTV", RatioRuleName = "STANDARD" } }
            },
            RatioRules = new Dictionary<string, object>
            {
                { "STANDARD", new { Name = "STANDARD", CreditsPerKiBUploaded = 1, CreditsPerKiBDownloaded = 1 } }
            },
            DirectoryRules = new Dictionary<string, object>
            {
                { "/0DAY", new { SectionName = "0DAY", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/MP3", new { SectionName = "MP3", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/XVID", new { SectionName = "XVID", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/STAFF", new { SectionName = "STAFF", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/SAFE", new { SectionName = "SAFE", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/TCV0DAY", new { SectionName = "TCV0DAY", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/TCVGAMES", new { SectionName = "TCVGAMES", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/TCVAPPS", new { SectionName = "TCVAPPS", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/TCVMP3", new { SectionName = "TCVMP3", AllowUpload = true, AllowDownload = true, AllowList = true } },
                { "/TCVTV", new { SectionName = "TCVTV", AllowUpload = true, AllowDownload = true, AllowList = true } }
            },
            Groups = new
            {
                admins = new { GroupName = "admins", Users = new[] { GAdminUser }, IsAdminGroup = true },
                users = new { GroupName = "users", Users = RaceAccounts.Select(a => a.UserName).Prepend(NormalUser).ToArray(), IsAdminGroup = false },
                ZDAY = new { GroupName = "0DAY", Users = new string[] { }, IsAdminGroup = false },
                Razor1911 = new { GroupName = "Razor1911", Users = new[] { "razor_pre" }, IsAdminGroup = false },
                FairLight = new { GroupName = "FairLight", Users = new[] { "fairlight_pre" }, IsAdminGroup = false },
                CLASS = new { GroupName = "CLASS", Users = new[] { "class_pre" }, IsAdminGroup = false },
                HOODLUM = new { GroupName = "HOODLUM", Users = new[] { "hoodlum_pre" }, IsAdminGroup = false },
                DEViANCE = new { GroupName = "DEViANCE", Users = new[] { "deviance_pre" }, IsAdminGroup = false }
            }
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ConfigPath, json);

        // Write default scripts
        await File.WriteAllTextAsync(Path.Combine(configDir, "rules", "credits.msl"), "");
        await File.WriteAllTextAsync(Path.Combine(configDir, "rules", "fxp.msl"), "");
        await File.WriteAllTextAsync(Path.Combine(configDir, "rules", "active.msl"), "");
        await File.WriteAllTextAsync(Path.Combine(configDir, "rules", "section-routing.msl"), "");
        await File.WriteAllTextAsync(Path.Combine(configDir, "rules", "site.msl"), "");
        await File.WriteAllTextAsync(Path.Combine(configDir, "rules", "user-rules.msl"), "");
        await File.WriteAllTextAsync(Path.Combine(configDir, "rules", "group-rules.msl"), "");
    }

    public async Task<AsyncFtpClient> CreateClientAsync(string user, string pass)
    {
        var client = new AsyncFtpClient("127.0.0.1", user, pass, Port);
        client.Config.EncryptionMode = FtpEncryptionMode.None;
        client.Config.DataConnectionConnectTimeout = 5000;
        client.Config.ConnectTimeout = 5000;
        client.Config.ReadTimeout = 5000;
        await client.Connect();
        return client;
    }

    public async Task DisposeAsync()
    {
        if (_server != null)
        {
            _server.Stop();
        }

        try { Directory.Delete(_tempDir, true); } catch { }
        _logger?.Dispose();
        await Task.CompletedTask;
    }
}

public sealed record TreasureCoveAccount(
    string UserName,
    string Password,
    string GroupName,
    string SectionName);

public sealed record FtpTestAccount(
    string UserName,
    string Password);
