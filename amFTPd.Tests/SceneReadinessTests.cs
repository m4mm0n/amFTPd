using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using amFTPd.Config;
using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Core;
using amFTPd.Core.Dupe;
using amFTPd.Db;
using amFTPd.Logging;
using FluentFTP;

namespace amFTPd.Tests;

public sealed class SceneReadinessTests
{
    [Fact]
    public void SectionAndDirectoryAccessPrefixMatching_UsesPathSegments()
    {
        var sections = new SectionManager(new[]
        {
            new amFTPd.Config.Ftpd.FtpSection("ROOT", "/", RatioUploadUnit: 1, RatioDownloadUnit: 1),
            new amFTPd.Config.Ftpd.FtpSection("0DAY", "/0DAY", RatioUploadUnit: 1, RatioDownloadUnit: 1),
            new amFTPd.Config.Ftpd.FtpSection("0DAYA", "/0DAYA", RatioUploadUnit: 1, RatioDownloadUnit: 1)
        });

        Assert.Equal("0DAY", sections.GetSectionForPath("/0DAY/file.bin").Name);
        Assert.Equal("0DAYA", sections.GetSectionForPath("/0DAYA/file.bin").Name);
        Assert.Equal("ROOT", sections.GetSectionForPath("/0DAYX/file.bin").Name);

        var directoryRules = new Dictionary<string, amFTPd.Config.Ftpd.RatioRules.DirectoryRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["/0DAY"] = new amFTPd.Config.Ftpd.RatioRules.DirectoryRule(
                "0DAY",
                "/0DAY",
                AllowUpload: true,
                AllowDownload: true,
                AllowList: true),
            ["/0DAYA"] = new amFTPd.Config.Ftpd.RatioRules.DirectoryRule(
                "0DAYA",
                "/0DAYA",
                AllowUpload: false,
                AllowDownload: false,
                AllowList: false)
        };

        var evaluator = new amFTPd.Core.Access.DirectoryAccessEvaluator(directoryRules);
        var accessA = evaluator.Evaluate("/0DAYA/release");
        Assert.False(accessA.CanUpload);

        var accessRoot = evaluator.Evaluate("/0DAYX/release");
        Assert.True(accessRoot.CanUpload);
    }

    [Fact]
    public async Task LongRunning_Workflow_AndRestart_PersistsSceneState()
    {
        await using var harness = await SceneSoakHarness.CreateAsync();

        await harness.StartAsync();

        for (var cycle = 1; cycle <= 6; cycle++)
        {
            var releasePath = $"/0DAY/LOOP.{cycle:D2}.ZLS";
            var file = $"{releasePath}/payload-{cycle:D2}.bin";
            var payload = Encoding.ASCII.GetBytes($"loop-cycle-{cycle}-{Guid.NewGuid():N}");

            using (var user = await harness.CreateClientAsync("testuser", "testpass"))
            {
                await user.CreateDirectory(releasePath);
                await user.UploadBytes(payload, file, FtpRemoteExists.Overwrite);

                var listing = await user.GetListing(releasePath);
                Assert.Contains(listing, entry => entry.Name == $"payload-{cycle:D2}.bin");

                var downloaded = await user.DownloadBytes(file, 0L);
                Assert.Equal(payload, downloaded);
            }

            using (var admin = await harness.CreateClientAsync(harness.AdminUser, harness.AdminPass))
            {
                var pre = await admin.Execute($"SITE PRE 0DAY {releasePath}");
                Assert.True(pre.Success, $"SITE PRE failed: {pre.Code} {pre.Message}");

                if (cycle % 2 == 0)
                {
                    var nuked = $"{releasePath}.NUKED";
                    var nuke = await admin.Execute($"SITE NUKE {releasePath} loop-cycle-{cycle}");
                    Assert.True(nuke.Success, $"SITE NUKE failed: {nuke.Code} {nuke.Message}");

                    var unnuke = await admin.Execute($"SITE UNNUKE {nuked} restored");
                    Assert.True(unnuke.Success, $"SITE UNNUKE failed: {unnuke.Code} {unnuke.Message}");
                }
            }
        }

        var userCreditsBeforeRestart = harness.Runtime.UserStore.FindUser("testuser")?.CreditsKb ?? -1;
        var dupeStoreBeforeRestart = harness.Runtime.DupeStore
            ?? throw new InvalidOperationException("Dupe store not initialized.");
        var dupeEntriesBeforeRestart = dupeStoreBeforeRestart.GetAll().Count();
        var dupeFilePath = harness.DupeFilePath;
        var persistedDupeCountBeforeRestart = GetPersistedDupeCount(dupeFilePath);
        Assert.True(File.Exists(dupeFilePath), $"Expected dupe file not found: {dupeFilePath}");
        Assert.True(new FileInfo(dupeFilePath).Length > 0, $"Dupe file should not be empty before restart: {dupeFilePath}");

        await harness.RestartAsync();

        var userCreditsAfterRestart = harness.Runtime.UserStore.FindUser("testuser")?.CreditsKb ?? -1;
        var dupeStoreAfterRestart = harness.Runtime.DupeStore
            ?? throw new InvalidOperationException("Dupe store not initialized after restart.");
        var dupeEntriesAfterRestart = dupeStoreAfterRestart.GetAll().Count();
        var persistedDupeCountAfterRestart = GetPersistedDupeCount(dupeFilePath);
        Assert.True(File.Exists(dupeFilePath), $"Expected dupe file not found after restart: {dupeFilePath}");
        Assert.True(new FileInfo(dupeFilePath).Length > 0, $"Dupe file should not be empty after restart: {dupeFilePath}");

        Assert.Equal(userCreditsBeforeRestart, userCreditsAfterRestart);
        Assert.True(
            dupeEntriesAfterRestart >= dupeEntriesBeforeRestart,
            $"Dupe entries lost across restart. memory before={dupeEntriesBeforeRestart}, after={dupeEntriesAfterRestart}, persisted before={persistedDupeCountBeforeRestart}, persisted after={persistedDupeCountAfterRestart}");

        using (var verify = await harness.CreateClientAsync("testuser", "testpass"))
        {
            Assert.True(await verify.DirectoryExists("/0DAY/LOOP.06.ZLS"));
        }
    }

    private sealed class SceneSoakHarness : IAsyncDisposable
    {
        private readonly string _root;
        private readonly string _dupeFilePath;
        private FtpServer? _server;
        private readonly QuickLogFtpLogger _logger = QuickLogFactory.CreateTestLogger(out _);
        private readonly string _configDir;
        private readonly string _dataDir;

        public string AdminUser { get; } = "admin";
        public string AdminPass { get; } = "adminpass";
        public int Port { get; private set; }
        public string ConfigPath { get; }
        public string DupeFilePath => _dupeFilePath;
        public AmFtpdRuntimeConfig Runtime { get; private set; } = null!;

        public string RootPath => Path.Combine(_root, "site-root");

        private SceneSoakHarness(string root)
        {
            _root = root;
            _configDir = Path.Combine(_root, "config");
            _dataDir = Path.Combine(_root, "data");
            ConfigPath = Path.Combine(_root, "amftpd.json");
            _dupeFilePath = Path.Combine(_dataDir, "dupes.json");
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(Path.Combine(RootPath, "0DAY"));
            Directory.CreateDirectory(_configDir);
            Directory.CreateDirectory(_dataDir);
            Directory.CreateDirectory(Path.Combine(_configDir, "rules"));
            Directory.CreateDirectory(Path.Combine(_configDir, "rules", "sections"));
        }

        public static Task<SceneSoakHarness> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "amftpd-scene-readiness",
                Guid.NewGuid().ToString("N"));

            return Task.FromResult(new SceneSoakHarness(root));
        }

        public async Task StartAsync()
        {
            Port = GetFreePort();

            await WriteConfigAsync();
            await SeedRuntimeAsync();
            await VerifyRuntimeSeedAsync();

            _server = new FtpServer(Runtime, _logger);
            _ = _server.StartAsync();

            await Task.Delay(500);
        }

        public async Task RestartAsync()
        {
            await StopAsync();
            await StartAsync();
        }

        private Task VerifyRuntimeSeedAsync()
        {
            Assert.True(
                Runtime.UserStore.TryAuthenticate(AdminUser, AdminPass, out var adminAccount, out var adminAuthError),
                $"Seed verification failed for admin: {adminAuthError}");
            Runtime.UserStore.OnLogout(adminAccount!);

            Assert.True(
                Runtime.UserStore.TryAuthenticate("testuser", "testpass", out var normalAccount, out var normalAuthError),
                $"Seed verification failed for testuser: {normalAuthError}");
            Runtime.UserStore.OnLogout(normalAccount!);

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _server?.Stop();
            _server = null;
            await Task.Delay(50);
        }

        public async Task<AsyncFtpClient> CreateClientAsync(string user, string pass)
        {
            var client = new AsyncFtpClient("127.0.0.1", user, pass, Port);
            client.Config.EncryptionMode = FtpEncryptionMode.None;
            client.Config.ConnectTimeout = 5000;
            client.Config.ReadTimeout = 5000;
            client.Config.DataConnectionConnectTimeout = 5000;
            await client.Connect();
            return client;
        }

        private async Task SeedRuntimeAsync()
        {
            Runtime = await AmFtpdConfigLoader.LoadAsync(ConfigPath, _logger);

            var userStore = Runtime.UserStore;
            var expectedAdmin = new FtpUser
            {
                UserName = AdminUser,
                PasswordHash = amFTPd.Security.PasswordHasher.HashPassword(AdminPass),
                PrimaryGroup = "admins",
                IsAdmin = true,
                IsSiteop = true,
                MaxCommandsPerMinuteOverride = 10_000
            };

            if (userStore.FindUser(AdminUser) is null)
            {
                Assert.True(
                    userStore.TryAddUser(expectedAdmin, out var addAdminError),
                    $"Failed to seed admin user: {addAdminError}");
            }
            else
            {
                Assert.True(
                    userStore.TryUpdateUser(expectedAdmin, out var updateAdminError),
                    $"Failed to seed admin user: {updateAdminError}");
            }

            var expectedNormal = new FtpUser
            {
                UserName = "testuser",
                PasswordHash = amFTPd.Security.PasswordHasher.HashPassword("testpass"),
                PrimaryGroup = "users",
                CreditsKb = 10_000,
                MaxCommandsPerMinuteOverride = 10_000
            };

            if (userStore.FindUser("testuser") is null)
            {
                Assert.True(
                    userStore.TryAddUser(expectedNormal, out var addNormalError),
                    $"Failed to seed normal user: {addNormalError}");
            }
            else
            {
                Assert.True(
                    userStore.TryUpdateUser(expectedNormal, out var updateNormalError),
                    $"Failed to seed normal user: {updateNormalError}");
            }

            if (Runtime.GroupStore is { } groupStore)
            {
                if (groupStore.FindGroup("admins") is null)
                {
                    Assert.True(
                        groupStore.TryAddGroup(
                            new Db.FtpGroup(
                                "admins",
                                "Admins",
                                [AdminUser],
                                new Dictionary<string, long>()),
                            out var addAdminGroupError),
                        $"Failed to seed admins group: {addAdminGroupError}");
                }

                if (groupStore.FindGroup("users") is null)
                {
                    Assert.True(
                        groupStore.TryAddGroup(
                            new Db.FtpGroup(
                                "users",
                                "Users",
                                ["testuser"],
                                new Dictionary<string, long>()),
                            out var addUsersGroupError),
                        $"Failed to seed users group: {addUsersGroupError}");
                }
            }
        }

        private async Task WriteConfigAsync()
        {
            var certPath = Path.Combine(_configDir, "cert.pfx");
            var certPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            CreateSelfSignedPfx(certPath, certPassword);

            var usersPath = Path.Combine(_dataDir, "users.json");
            var groupsPath = Path.Combine(_dataDir, "groups.json");
            var sectionsPath = Path.Combine(_dataDir, "sections.json");
            var dupePath = Path.Combine(_dataDir, "dupes.json");
            var dupeContentPath = Path.Combine(_dataDir, "dupe-contents.json");

            await File.WriteAllTextAsync(sectionsPath, """
                [
                  { "Name": "0DAY", "RatioRuleName": "STANDARD", "VirtualRoot": "/0DAY" }
                ]
                """);

            var config = new
            {
                Server = new
                {
                    BindAddress = "127.0.0.1",
                    Port = Port,
                    PassivePortStart = 30000,
                    PassivePortEnd = 30110,
                    RootPath = RootPath,
                    WelcomeMessage = "amFTPd Scene Readiness",
                    AllowAnonymous = false,
                    RequireTlsForAuth = false,
                    DataChannelProtectionDefault = "C",
                    AllowActiveMode = true,
                    AllowFxp = false,
                    MaxCommandsPerMinute = 10000
                },
                Tls = new
                {
                    PfxPath = certPath,
                    PfxPassword = certPassword,
                    SubjectName = "CN=amFTPd Soak"
                },
                Ident = new
                {
                    Enabled = false,
                    Required = false,
                    TimeoutMs = 10
                },
                Storage = new
                {
                    BaseDirectory = _root,
                    MasterPassword = "scene-readiness-master-password",
                    UserStoreBackend = "json",
                    UsersDbPath = usersPath,
                    GroupsDbPath = groupsPath,
                    SectionsDbPath = Path.Combine(_dataDir, "sections.db"),
                    DupeStorePath = dupePath,
                    DupeContentDbPath = dupeContentPath,
                    RulesPath = Path.Combine(_configDir, "rules"),
                    ScriptsConfigPath = Path.Combine(_configDir, "scripts.json"),
                    SectionsPath = sectionsPath
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
                    ["0DAY"] = new
                    {
                        SectionName = "0DAY",
                        RatioRuleName = "STANDARD"
                    }
                },
                RatioRules = new Dictionary<string, object>
                {
                    ["STANDARD"] = new
                    {
                        Name = "STANDARD",
                        CreditsPerKiBUploaded = 1,
                        CreditsPerKiBDownloaded = 1
                    }
                },
                DirectoryRules = new Dictionary<string, object>
                {
                    ["/0DAY"] = new { SectionName = "0DAY", AllowUpload = true, AllowDownload = true, AllowList = true }
                },
                Groups = new Dictionary<string, object>
                {
                    ["admins"] = new { GroupName = "admins", Users = new[] { AdminUser }, IsAdminGroup = true },
                    ["users"] = new { GroupName = "users", Users = new[] { "testuser" }, IsAdminGroup = false }
                }
            };

            await File.WriteAllTextAsync(ConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

            await File.WriteAllTextAsync(Path.Combine(_configDir, "scripts.json"), """
                {
                  "active": "",
                  "credits": "",
                  "fxp": "",
                  "site": "",
                  "section-routing": ""
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(_configDir, "rules", "credits.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(_configDir, "rules", "fxp.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(_configDir, "rules", "active.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(_configDir, "rules", "site.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(_configDir, "rules", "section-routing.msl"), "");
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static void CreateSelfSignedPfx(string path, string password)
        {
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest(
                "CN=amFTPd Soak",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(2));

            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static int GetPersistedDupeCount(string path)
    {
        if (!File.Exists(path))
            return 0;

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        try
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            };

            return JsonSerializer.Deserialize<JsonElement>(json, options) switch
            {
                { ValueKind: JsonValueKind.Array } arr => arr.GetArrayLength(),
                _ => 0
            };
        }
        catch
        {
            return -1;
        }
    }
}
