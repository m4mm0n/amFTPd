using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using amFTPd.Config.Daemon;
using amFTPd.Core;
using amFTPd.Core.Linting;
using amFTPd.Logging;

namespace amFTPd.Tests;

public sealed class ProductionReadinessTests
{
    [Fact]
    public async Task ConfigOnlySections_LoadIntoRuntimeWithoutSectionsPath()
    {
        using var layout = await ProductionConfigLayout.CreateAsync(includeSectionsPath: false);

        using var logger = QuickLogFactory.CreateTestLogger(out _);
        var runtime = await AmFtpdConfigLoader.LoadAsync(
            layout.ConfigPath,
            logger);

        var sections = runtime.Sections.GetSections();

        Assert.Contains(sections, section =>
            section.Name == "0DAY" &&
            section.VirtualRoot == "/0DAY" &&
            section.RatioSection == "SCENE-1:3");

        Assert.Contains(sections, section =>
            section.Name == "MP3" &&
            section.VirtualRoot == "/MP3" &&
            section.FreeLeech);
    }

    [Fact]
    public async Task ServerCommandRateLimitSetting_FromConfig_AppliesToRuntime()
    {
        using var layout = await ProductionConfigLayout.CreateAsync(
            includeSectionsPath: false,
            maxCommandsPerMinute: 10_000);

        using var logger = QuickLogFactory.CreateTestLogger(out _);
        var runtime = await AmFtpdConfigLoader.LoadAsync(
            layout.ConfigPath,
            logger);

        Assert.Equal(10_000, runtime.FtpConfig.MaxCommandsPerMinute);
    }

    [Fact]
    public async Task ProductionLikeConfig_ValidatesWithoutWarningsOrErrors()
    {
        using var layout = await ProductionConfigLayout.CreateAsync(includeSectionsPath: false);

        var result = ConfigValidator.Validate(layout.ConfigPath);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Severity is LintSeverity.Warning or LintSeverity.Error);
    }

    [Fact]
    public async Task ProductionLikeConfig_ServerStartsAndAcceptsControlConnection()
    {
        var port = GetFreeTcpPort();
        using var layout = await ProductionConfigLayout.CreateAsync(
            includeSectionsPath: false,
            port: port);

        using var logger = QuickLogFactory.CreateTestLogger(out _);
        var runtime = await AmFtpdConfigLoader.LoadAsync(
            layout.ConfigPath,
            logger);

        var server = new FtpServer(runtime, logger);
        var serverTask = server.StartAsync();

        try
        {
            using var client = await ConnectWithRetryAsync(port, TimeSpan.FromSeconds(5));
            using var reader = new StreamReader(client.GetStream());

            var banner = await reader.ReadLineAsync();

            Assert.StartsWith("220", banner);
        }
        finally
        {
            server.Stop();
            await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    private static int GetFreeTcpPort()
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

    private static async Task<TcpClient> ConnectWithRetryAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                return client;
            }
            catch (Exception ex)
            {
                lastError = ex;
                client.Dispose();
                await Task.Delay(100);
            }
        }

        throw new TimeoutException(
            $"Timed out connecting to amFTPd on 127.0.0.1:{port}.",
            lastError);
    }

    private sealed class ProductionConfigLayout : IDisposable
    {
        private readonly string _root;

        private ProductionConfigLayout(string root)
        {
            _root = root;
            ConfigPath = Path.Combine(root, "amftpd.json");
        }

        public string ConfigPath { get; }

        public static async Task<ProductionConfigLayout> CreateAsync(
            bool includeSectionsPath,
            int maxCommandsPerMinute = 240,
            int port = 2121)
        {
            var root = Path.Combine(Path.GetTempPath(), "amftpd-prod-ready", Guid.NewGuid().ToString("N"));
            var layout = new ProductionConfigLayout(root);

            var siteRoot = Path.Combine(root, "site-root");
            var configRoot = Path.Combine(root, "config");
            var dataRoot = Path.Combine(root, "data");
            var rulesRoot = Path.Combine(root, "rules");
            var sectionsPath = Path.Combine(configRoot, "sections.json");
            var certPath = Path.Combine(configRoot, "cert.pfx");
            var pfxPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            Directory.CreateDirectory(siteRoot);
            Directory.CreateDirectory(Path.Combine(siteRoot, "0DAY"));
            Directory.CreateDirectory(Path.Combine(siteRoot, "MP3"));
            Directory.CreateDirectory(configRoot);
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(rulesRoot);

            await File.WriteAllTextAsync(sectionsPath, """
            [
              { "Name": "0DAY", "VirtualRoot": "/0DAY", "RatioSection": "SCENE-1:3" },
              { "Name": "MP3", "VirtualRoot": "/MP3", "RatioSection": "FREE", "FreeLeech": true }
            ]
            """);

            CreateSelfSignedPfx(certPath, pfxPassword);

            var storage = new Dictionary<string, object?>
            {
                ["BaseDirectory"] = root,
                ["MasterPassword"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["UserStoreBackend"] = "json",
                ["UsersDbPath"] = Path.Combine(dataRoot, "users.json"),
                ["GroupsDbPath"] = Path.Combine(dataRoot, "groups.db"),
                ["SectionsDbPath"] = Path.Combine(dataRoot, "sections.db"),
                ["DupeDbPath"] = Path.Combine(dataRoot, "dupes.db"),
                ["DupeContentDbPath"] = Path.Combine(dataRoot, "dupe-contents.db"),
                ["RulesPath"] = rulesRoot,
                ["ScriptsConfigPath"] = Path.Combine(dataRoot, "scripts.json")
            };

            if (includeSectionsPath)
                storage["SectionsPath"] = sectionsPath;

            var config = new
            {
                Server = new
                {
                    BindAddress = "127.0.0.1",
                    Port = port,
                    PassivePortStart = 50100,
                    PassivePortEnd = 50110,
                    RootPath = siteRoot,
                    WelcomeMessage = "amFTPd readiness test",
                    AllowAnonymous = false,
                    RequireTlsForAuth = true,
                    DataChannelProtectionDefault = "P",
                    AllowActiveMode = false,
                    AllowFxp = false,
                    MaxCommandsPerMinute = maxCommandsPerMinute
                },
                Tls = new
                {
                    PfxPath = certPath,
                    PfxPassword = pfxPassword,
                    SubjectName = "CN=amFTPd Test"
                },
                Ident = new
                {
                    Enabled = false,
                    Required = false,
                    TimeoutMs = 10
                },
                Storage = storage,
                Vfs = new
                {
                    Provider = "physical",
                    Mounts = new[]
                    {
                        new { VirtualRoot = "/", PhysicalPath = siteRoot, ReadOnly = false }
                    }
                },
                Sections = new Dictionary<string, object>
                {
                    ["0DAY"] = new { SectionName = "0DAY", RatioRuleName = "SCENE-1:3" },
                    ["MP3"] = new { SectionName = "MP3", RatioRuleName = "FREE" }
                },
                RatioRules = new Dictionary<string, object>
                {
                    ["SCENE-1:3"] = new { Name = "SCENE-1:3", CreditsPerKiBUploaded = 3, CreditsPerKiBDownloaded = 1 },
                    ["FREE"] = new { Name = "FREE", CreditsPerKiBUploaded = 0, CreditsPerKiBDownloaded = 0, IsFree = true }
                },
                DirectoryRules = new Dictionary<string, object>
                {
                    ["/0DAY"] = new { SectionName = "0DAY", AllowUpload = true, AllowDownload = true, AllowList = true },
                    ["/MP3"] = new { SectionName = "MP3", AllowUpload = true, AllowDownload = true, AllowList = true }
                },
                Groups = new Dictionary<string, object>
                {
                    ["USERS"] = new { Description = "Standard users", RatioMultiply = 1.0, UploadBonus = 1.0 },
                    ["SITEOP"] = new { Description = "Site operators", IsSiteOp = true }
                }
            };

            await File.WriteAllTextAsync(
                layout.ConfigPath,
                JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

            return layout;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup. A failed cleanup should not hide the test result.
            }
        }

        private static void CreateSelfSignedPfx(string path, string password)
        {
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest(
                "CN=amFTPd Test",
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
    }
}
