using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using amFTPd.Config.Daemon;
using amFTPd.Core.Migration;
using amFTPd.Logging;
using amFTPd.Utils.Tools;

namespace amFTPd.Tests;

public sealed class MigrationCliAcceptanceTests
{
    [Fact]
    public async Task CheckMigration_CLI_IoFixture_ReturnsOkAndWritesReport()
    {
        var sourceRoot = BuildIoFixture();
        var reportPath = Path.Combine(Path.GetTempPath(), $"amftpd-io-migration-cli-{Guid.NewGuid():N}.json");
        using var layout = await MigrationCliLayout.CreateAsync();

        try
        {
            await ImportFixtureAsync(layout.ConfigPath, sourceRoot);

            var result = await RunMigrationCheckAsync(layout.ConfigPath, sourceRoot, reportPath);
            Assert.True(
                result.ExitCode == 0,
                $"Expected clean migration check. Exit={result.ExitCode}\nSTDOUT:\n{result.StandardOut}\nSTDERR:\n{result.StandardError}");

            var report = await ReadReportAsync(reportPath);
            Assert.Equal("Ok", report.Status);
            Assert.Empty(report.Users.MissingInTarget);
            Assert.Empty(report.Groups.MissingInTarget);
            Assert.Empty(report.Dupes.MissingInTarget);
            Assert.Empty(report.Nukes.MissingNukeFlag);
            Assert.Empty(report.Dupes.UnknownSections);
            Assert.Empty(report.Nukes.UnknownSections);
        }
        finally
        {
            try { Directory.Delete(sourceRoot, true); } catch { }
            try { File.Delete(reportPath); } catch { }
        }
    }

    [Fact]
    public async Task CheckMigration_CLI_GlFixture_ReturnsOkAndWritesReport()
    {
        var sourceRoot = BuildGlFixture();
        var reportPath = Path.Combine(Path.GetTempPath(), $"amftpd-gl-migration-cli-{Guid.NewGuid():N}.json");
        using var layout = await MigrationCliLayout.CreateAsync();

        try
        {
            await ImportFixtureAsync(layout.ConfigPath, sourceRoot);

            var result = await RunMigrationCheckAsync(layout.ConfigPath, sourceRoot, reportPath);
            Assert.True(
                result.ExitCode == 0,
                $"Expected clean migration check. Exit={result.ExitCode}\nSTDOUT:\n{result.StandardOut}\nSTDERR:\n{result.StandardError}");

            var report = await ReadReportAsync(reportPath);
            Assert.Equal("Ok", report.Status);
            Assert.Empty(report.Users.MissingInTarget);
            Assert.Empty(report.Groups.MissingInTarget);
            Assert.Empty(report.Dupes.MissingInTarget);
            Assert.Empty(report.Nukes.MissingNukeFlag);
            Assert.Empty(report.Dupes.UnknownSections);
            Assert.Empty(report.Nukes.UnknownSections);
        }
        finally
        {
            try { Directory.Delete(sourceRoot, true); } catch { }
            try { File.Delete(reportPath); } catch { }
        }
    }

    private static async Task ImportFixtureAsync(string configPath, string sourceRoot)
    {
        using var logger = QuickLogFactory.CreateTestLogger(out _);
        var runtime = await AmFtpdConfigLoader.LoadAsync(configPath, logger);

        try
        {
            await GlIoImport.ImportWithSummaryAsync(
                sourceRoot: sourceRoot,
                sections: runtime.Sections,
                users: runtime.UserStore,
                groups: runtime.GroupStore!,
                flavor: null,
                preRegistry: runtime.PreRegistry,
                dupeStore: runtime.DupeStore,
                zipscript: runtime.Zipscript);
        }
        finally
        {
            if (runtime.DupeStore is IDisposable dupeStore)
                dupeStore.Dispose();
        }
    }

    private static async Task<(int ExitCode, string StandardOut, string StandardError)> RunMigrationCheckAsync(
        string configPath,
        string sourceRoot,
        string reportPath)
    {
        var repoRoot = FindRepositoryRoot();
        var dllPath = Path.Combine(repoRoot, "amFTPd", "bin", "Release", "net10.0", "amFTPd.dll");
        var projectPath = Path.Combine(repoRoot, "amFTPd", "amFTPd.csproj");

        var (fileName, arguments) = File.Exists(dllPath)
            ? ("dotnet", $"\"{dllPath}\" \"{configPath}\" --check-migration \"{sourceRoot}\" --json \"{reportPath}\"")
            : ("dotnet", $"run --project \"{projectPath}\" -- \"{configPath}\" --check-migration \"{sourceRoot}\" --json \"{reportPath}\"");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdout = await process!.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout, stderr);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "amFTPd.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static async Task<MigrationReport> ReadReportAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<MigrationReport>(json)
               ?? throw new InvalidOperationException("Failed to parse migration report JSON.");
    }

    private static string BuildIoFixture()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "amftpd-io-migration-cli",
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "userfiles"));
        Directory.CreateDirectory(Path.Combine(root, "groups"));
        Directory.CreateDirectory(Path.Combine(root, "groups", "USERS"));
        Directory.CreateDirectory(Path.Combine(root, "groups", "RIPPERS"));
        Directory.CreateDirectory(Path.Combine(root, "groups", "SCRIPTERS"));

        File.WriteAllText(Path.Combine(root, "userfiles", "iomanager"), "SITEOP ADMIN");
        File.WriteAllText(Path.Combine(root, "userfiles", "iopacker"), "SITEOP");
        File.WriteAllText(Path.Combine(root, "userfiles", "ioni"), "NORATIO");
        File.WriteAllText(Path.Combine(root, "userfiles", "ioupload"), "ADMIN");

        File.WriteAllText(
            Path.Combine(root, "pre.log"),
            "PRE MP3 IO-ALPHA-ONE RIPPERS 1700000010\n" +
            "PRE XVID IO-XVID-TWO SCRIPTERS 1700000020\n");

        File.WriteAllText(
            Path.Combine(root, "ioDUPE.db"),
            "MP3|IO-ALPHA-ONE|RIPPERS|1700000030|1048576\n" +
            "XVID|IO-XVID-TWO|SCRIPTERS|1700000040|2097152|NUKED:bad\n");

        File.WriteAllText(
            Path.Combine(root, "nuke.log"),
            "NUKE MP3 IO-ALPHA-ONE x2 bad staff 1700000050\n" +
            "NUKE XVID IO-XVID-TWO x5 corrupt staff2 1700000060\n");

        return root;
    }

    private static string BuildGlFixture()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "amftpd-gl-migration-cli",
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "ftp-data", "misc"));

        File.WriteAllText(
            Path.Combine(root, "passwd"),
            "gladmin:*:1:2:16N\n" +
            "glsite:*:1:2:1\n" +
            "glnoratio:*:1:2:6N\n" +
            "gluser:*:1:2:\n");

        File.WriteAllText(
            Path.Combine(root, "group"),
            "DEFAULT:0:0:\n" +
            "RUSERS:0:0:\n" +
            "RAPTORS:0:0:\n");

        File.WriteAllText(
            Path.Combine(root, "glftpd.pre"),
            "MP3 GL-ALPHA-ONE RUSERS 1700000110\n" +
            "0DAY GL-BETA-TWO RAPTORS 1700000120\n");

        File.WriteAllText(
            Path.Combine(root, "ftp-data/misc/dupefile.txt"),
            "MP3|GL-ALPHA-ONE|RUSERS|1700000130|3145728\n" +
            "0DAY|GL-BETA-TWO|RAPTORS|1700000140|4194304\n");

        File.WriteAllText(
            Path.Combine(root, "glftpd.nuke"),
            "NUKE MP3 GL-ALPHA-ONE 3 bad staffgl 1700000150\n" +
            "NUKE 0DAY GL-BETA-TWO 5 severe reason staffgl2 1700000160\n");

        return root;
    }

    private sealed class MigrationCliLayout : IDisposable
    {
        private readonly string _root;

        private MigrationCliLayout(string root)
        {
            _root = root;
            ConfigPath = Path.Combine(root, "amftpd.json");
        }

        public string ConfigPath { get; }

        public static async Task<MigrationCliLayout> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "amftpd-migration-cli-layout", Guid.NewGuid().ToString("N"));
            var layout = new MigrationCliLayout(root);

            var siteRoot = Path.Combine(root, "site-root");
            var configRoot = Path.Combine(root, "config");
            var dataRoot = Path.Combine(root, "data");
            var rulesRoot = Path.Combine(configRoot, "rules");
            var sectionsPath = Path.Combine(configRoot, "sections.json");
            var certPath = Path.Combine(configRoot, "cert.pfx");
            var pfxPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            Directory.CreateDirectory(siteRoot);
            Directory.CreateDirectory(Path.Combine(siteRoot, "0DAY"));
            Directory.CreateDirectory(Path.Combine(siteRoot, "MP3"));
            Directory.CreateDirectory(Path.Combine(siteRoot, "XVID"));
            Directory.CreateDirectory(configRoot);
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(rulesRoot);

            await File.WriteAllTextAsync(sectionsPath, """
            [
              { "Name": "DEFAULT", "RatioRuleName": "STANDARD", "DirectoryRules": [] },
              { "Name": "0DAY", "RatioRuleName": "STANDARD", "DirectoryRules": [] },
              { "Name": "MP3", "RatioRuleName": "STANDARD", "DirectoryRules": [] },
              { "Name": "XVID", "RatioRuleName": "STANDARD", "DirectoryRules": [] }
            ]
            """);

            CreateSelfSignedPfx(certPath, pfxPassword);

            await File.WriteAllTextAsync(Path.Combine(configRoot, "scripts.json"), """
            {
              "active": "",
              "credits": "",
              "fxp": "",
              "site": "",
              "section-routing": ""
            }
            """);

            await File.WriteAllTextAsync(Path.Combine(rulesRoot, "credits.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(rulesRoot, "fxp.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(rulesRoot, "active.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(rulesRoot, "site.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(rulesRoot, "section-routing.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(rulesRoot, "user-rules.msl"), "");
            await File.WriteAllTextAsync(Path.Combine(rulesRoot, "group-rules.msl"), "");

            var config = new
            {
                Server = new
                {
                    BindAddress = "127.0.0.1",
                    Port = 2121,
                    PassivePortStart = 40100,
                    PassivePortEnd = 40110,
                    RootPath = siteRoot,
                    WelcomeMessage = "amFTPd migration CLI",
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
                    PfxPassword = pfxPassword,
                    SubjectName = "CN=amFTPd Migration CLI"
                },
                Ident = new
                {
                    Enabled = false,
                    Required = false,
                    TimeoutMs = 10
                },
                Storage = new
                {
                    BaseDirectory = root,
                    MasterPassword = "migration-cli-master-password",
                    UserStoreBackend = "json",
                    UsersDbPath = Path.Combine(dataRoot, "users.json"),
                    GroupsDbPath = Path.Combine(dataRoot, "groups.db"),
                    SectionsDbPath = Path.Combine(dataRoot, "sections.db"),
                    DupeStoreBackend = "file",
                    DupeStorePath = Path.Combine(dataRoot, "dupes.json"),
                    DupeContentDbPath = Path.Combine(dataRoot, "dupe-contents.db"),
                    RulesPath = rulesRoot,
                    ScriptsConfigPath = Path.Combine(configRoot, "scripts.json"),
                    SectionsPath = sectionsPath
                },
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
                    ["DEFAULT"] = new { SectionName = "DEFAULT", RatioRuleName = "STANDARD" },
                    ["0DAY"] = new { SectionName = "0DAY", RatioRuleName = "STANDARD" },
                    ["MP3"] = new { SectionName = "MP3", RatioRuleName = "STANDARD" },
                    ["XVID"] = new { SectionName = "XVID", RatioRuleName = "STANDARD" }
                },
                RatioRules = new Dictionary<string, object>
                {
                    ["STANDARD"] = new { Name = "STANDARD", CreditsPerKiBUploaded = 1, CreditsPerKiBDownloaded = 1 }
                },
                DirectoryRules = new Dictionary<string, object>
                {
                    ["/0DAY"] = new { SectionName = "0DAY", AllowUpload = true, AllowDownload = true, AllowList = true },
                    ["/MP3"] = new { SectionName = "MP3", AllowUpload = true, AllowDownload = true, AllowList = true },
                    ["/XVID"] = new { SectionName = "XVID", AllowUpload = true, AllowDownload = true, AllowList = true }
                },
                Groups = new Dictionary<string, object>
                {
                    ["DEFAULT"] = new { GroupName = "DEFAULT", Users = Array.Empty<string>(), IsAdminGroup = false },
                    ["USERS"] = new { GroupName = "USERS", Users = Array.Empty<string>(), IsAdminGroup = false },
                    ["RIPPERS"] = new { GroupName = "RIPPERS", Users = Array.Empty<string>(), IsAdminGroup = false },
                    ["SCRIPTERS"] = new { GroupName = "SCRIPTERS", Users = Array.Empty<string>(), IsAdminGroup = false },
                    ["RUSERS"] = new { GroupName = "RUSERS", Users = Array.Empty<string>(), IsAdminGroup = false },
                    ["RAPTORS"] = new { GroupName = "RAPTORS", Users = Array.Empty<string>(), IsAdminGroup = false }
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
            }
        }

        private static void CreateSelfSignedPfx(string path, string password)
        {
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest(
                "CN=amFTPd Migration CLI",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(2));

            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        }
    }
}
