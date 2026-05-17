using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Import;
using amFTPd.Core.Migration;
using amFTPd.Logging;
using amFTPd.Utils.Tools;

namespace amFTPd.Tests;

public sealed class MigrationFixtureTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public MigrationFixtureTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ImportIoFixture_AndValidateMigrationReport_AreOk()
    {
        var sourceRoot = BuildIoFixture();
        try
        {
            NormalizeMigratedTestUsers(_fixture.Runtime.UserStore);

            await using var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);
            var import = await admin.Execute($"SITE IMPORT IOFTPD {sourceRoot}");
            Assert.True(import.Success, $"SITE IMPORT IOFTPD failed: {import.Code} {import.Message}");

            var importDupe = await admin.Execute($"SITE IMPORTDUPE IOFTPD {sourceRoot} MERGE");
            Assert.True(importDupe.Success, $"SITE IMPORTDUPE IOFTPD failed: {importDupe.Code} {importDupe.Message}");

            var report = await MigrationValidator.ValidateAsync(sourceRoot, _fixture.Runtime);
            Assert.Equal("Ok", report.Status);
            Assert.Empty(report.Users.MissingInTarget);
            Assert.Empty(report.Groups.MissingInTarget);
            Assert.Empty(report.Dupes.MissingInTarget);
            Assert.Empty(report.Nukes.MissingNukeFlag);
            Assert.Empty(report.Dupes.UnknownSections);
            Assert.Equal(0, report.Dupes.UnknownSectionRecordCount);
            Assert.Empty(report.Nukes.UnknownSections);
            Assert.Equal(0, report.Nukes.UnknownSectionRecordCount);

            var runtimeUsers = _fixture.Runtime.UserStore;
            foreach (var expected in ExpectedIoUsers())
            {
                var imported = runtimeUsers.FindUser(expected.Name);
                Assert.NotNull(imported);
                Assert.Equal(expected.IsSiteop, imported!.IsSiteop);
                Assert.Equal(expected.IsAdmin, imported.IsAdmin);
                Assert.Equal(expected.IsNoRatio, imported.IsNoRatio);
                Assert.Equal("USERS", imported.PrimaryGroup);
            }

            var usersGroup = _fixture.Runtime.GroupStore?.FindGroup("USERS");
            Assert.NotNull(usersGroup);
            foreach (var expected in ExpectedIoUsers())
            {
                Assert.Contains(expected.Name, usersGroup!.Users, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var expected in ExpectedIoGroups())
            {
                var group = _fixture.Runtime.GroupStore!.FindGroup(expected);
                Assert.NotNull(group);
            }

            AssertAllPreEntriesImported(_fixture.Runtime.PreRegistry, ExpectedIoPres());
            AssertAllDupeEntriesImported(_fixture.Runtime.DupeStore, ExpectedIoDupes());
            AssertAllNukesImported(_fixture.Runtime.Zipscript!, ExpectedIoNukes());
        }
        finally
        {
            try { Directory.Delete(sourceRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task ImportGlFixture_AndValidateMigrationReport_AreOk()
    {
        var sourceRoot = BuildGlFixture();
        try
        {
            NormalizeMigratedTestUsers(_fixture.Runtime.UserStore);

            await using var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);
            var import = await admin.Execute($"SITE IMPORT GLFTP {sourceRoot}");
            Assert.True(import.Success, $"SITE IMPORT GLFTP failed: {import.Code} {import.Message}");

            var importDupe = await admin.Execute($"SITE IMPORTDUPE GLFTPD {sourceRoot} MERGE");
            Assert.True(importDupe.Success, $"SITE IMPORTDUPE GLFTPD failed: {importDupe.Code} {importDupe.Message}");

            var report = await MigrationValidator.ValidateAsync(sourceRoot, _fixture.Runtime);
            Assert.Equal("Ok", report.Status);
            Assert.Empty(report.Users.MissingInTarget);
            Assert.Empty(report.Groups.MissingInTarget);
            Assert.Empty(report.Dupes.MissingInTarget);
            Assert.Empty(report.Nukes.MissingNukeFlag);
            Assert.Empty(report.Dupes.UnknownSections);
            Assert.Equal(0, report.Dupes.UnknownSectionRecordCount);
            Assert.Empty(report.Nukes.UnknownSections);
            Assert.Equal(0, report.Nukes.UnknownSectionRecordCount);

            var runtimeUsers = _fixture.Runtime.UserStore;
            foreach (var expected in ExpectedGlUsers())
            {
                var imported = runtimeUsers.FindUser(expected.Name);
                Assert.NotNull(imported);
                Assert.Equal(expected.IsSiteop, imported!.IsSiteop);
                Assert.Equal(expected.IsAdmin, imported.IsAdmin);
                Assert.Equal(expected.IsNoRatio, imported.IsNoRatio);
                Assert.Equal("DEFAULT", imported.PrimaryGroup);
            }

            var defaultGroup = _fixture.Runtime.GroupStore?.FindGroup("DEFAULT");
            Assert.NotNull(defaultGroup);
            foreach (var expected in ExpectedGlUsers())
            {
                Assert.Contains(expected.Name, defaultGroup!.Users, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var expected in ExpectedGlGroups())
            {
                var group = _fixture.Runtime.GroupStore!.FindGroup(expected);
                Assert.NotNull(group);
            }

            AssertAllPreEntriesImported(_fixture.Runtime.PreRegistry, ExpectedGlPres());
            AssertAllDupeEntriesImported(_fixture.Runtime.DupeStore, ExpectedGlDupes());
            AssertAllNukesImported(_fixture.Runtime.Zipscript!, ExpectedGlNukes());
        }
        finally
        {
            try { Directory.Delete(sourceRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task ImportGlFixture_WithAliasSections_AndValidateMigrationReport_TracksUnknownSections()
    {
        var sourceRoot = BuildAliasHeavyGlFixture();
        try
        {
            var runtime = await CreateAliasAwareMigrationRuntimeAsync(_fixture.ConfigPath);
            var result = await GlIoImport.ImportWithSummaryAsync(
                sourceRoot: sourceRoot,
                sections: runtime.Sections,
                users: runtime.UserStore,
                groups: runtime.GroupStore!,
                flavor: ImportFlavor.GlFtpd,
                preRegistry: runtime.PreRegistry,
                dupeStore: runtime.DupeStore,
                zipscript: runtime.Zipscript);

            Assert.Equal(ImportFlavor.GlFtpd, result.Flavor);
            Assert.Equal(3, result.ParsedDupes);
            Assert.Equal(2, result.ImportedDupes);
            Assert.Equal(3, result.ParsedNukes);
            Assert.Equal(2, result.ImportedNukes);
            Assert.Equal(1, result.UnknownSectionsCount);
            Assert.Contains("UNMAPPED", result.UnknownSections);

            var report = await MigrationValidator.ValidateAsync(sourceRoot, runtime);
            Assert.Equal("Warnings", report.Status);
            Assert.Equal(1, report.Dupes.UnknownSectionRecordCount);
            Assert.Single(report.Dupes.UnknownSections);
            Assert.Contains("UNMAPPED", report.Dupes.UnknownSections);
            Assert.Equal(1, report.Nukes.UnknownSectionRecordCount);
            Assert.Single(report.Nukes.UnknownSections);
            Assert.Contains("UNMAPPED", report.Nukes.UnknownSections);

            Assert.Empty(report.Dupes.MissingInTarget);
            Assert.Equal(0, report.Dupes.MissingCount);
            Assert.Empty(report.Nukes.MissingNukeFlag);
        }
        finally
        {
            try { Directory.Delete(sourceRoot, true); } catch { }
        }
    }

    private static void NormalizeMigratedTestUsers(amFTPd.Config.Ftpd.IUserStore users)
    {
        var admin = users.FindUser("admin");
        if (admin is not null)
            users.TryUpdateUser(admin with { CreditsKb = 0 }, out _);

        var testuser = users.FindUser("testuser");
        if (testuser is not null)
            users.TryUpdateUser(testuser with { CreditsKb = 0 }, out _);
    }

    private static string BuildIoFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "amftpd-io-migration", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);

        var groups = new[] { "USERS", "RIPPERS", "SCRIPTERS" };

        Directory.CreateDirectory(Path.Combine(root, "userfiles"));
        Directory.CreateDirectory(Path.Combine(root, "groups"));
        foreach (var group in groups)
            Directory.CreateDirectory(Path.Combine(root, "groups", group));

        File.WriteAllText(Path.Combine(root, "userfiles", "iomanager"), "SITEOP ADMIN");
        File.WriteAllText(Path.Combine(root, "userfiles", "iopacker"), "SITEOP");
        File.WriteAllText(Path.Combine(root, "userfiles", "ioni"), "NORATIO");
        File.WriteAllText(Path.Combine(root, "userfiles", "ioupload"), "ADMIN");

        File.WriteAllText(Path.Combine(root, "pre.log"), """
            PRE MP3 IO-ALPHA-ONE RIPPERS 1700000010
            PRE XVID IO-XVID-TWO SCRIPTERS 1700000020
            """);

        File.WriteAllText(Path.Combine(root, "ioDUPE.db"), """
            MP3|IO-ALPHA-ONE|RIPPERS|1700000030|1048576
            XVID|IO-XVID-TWO|SCRIPTERS|1700000040|2097152|NUKED:bad
            """);

        File.WriteAllText(Path.Combine(root, "nuke.log"), """
            NUKE MP3 IO-ALPHA-ONE x2 bad staff 1700000050
            NUKE XVID IO-XVID-TWO x5 corrupt staff2 1700000060
            """);
        return root;
    }

    private static string BuildGlFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "amftpd-gl-migration", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "ftp-data", "misc"));

        var groups = new[] { "DEFAULT", "RUSERS", "RAPTORS" };

        File.WriteAllText(Path.Combine(root, "passwd"), """
            gladmin:*:1:2:16N
            glsite:*:1:2:1
            glnoratio:*:1:2:6N
            gluser:*:1:2:
            """);

        var groupLines = new List<string>();
        foreach (var group in groups)
        {
            groupLines.Add($"{group}:0:0:");
        }

        File.WriteAllText(
            Path.Combine(root, "group"),
            string.Join(Environment.NewLine, groupLines));

        File.WriteAllText(Path.Combine(root, "glftpd.pre"), """
            MP3 GL-ALPHA-ONE RUSERS 1700000110
            0DAY GL-BETA-TWO RAPTORS 1700000120
            """);

        File.WriteAllText(Path.Combine(root, "ftp-data/misc/dupefile.txt"), """
            MP3|GL-ALPHA-ONE|RUSERS|1700000130|3145728
            0DAY|GL-BETA-TWO|RAPTORS|1700000140|4194304
            """);

        File.WriteAllText(Path.Combine(root, "glftpd.nuke"), """
            NUKE MP3 GL-ALPHA-ONE 3 bad staffgl 1700000150
            NUKE 0DAY GL-BETA-TWO 5 severe reason staffgl2 1700000160
            """);

        return root;
    }

    private static string BuildAliasHeavyGlFixture()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "amftpd-gl-alias-migration",
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "ftp-data", "misc"));

        var groups = new[] { "DEFAULT", "RUSERS", "RAPTORS" };

        File.WriteAllText(Path.Combine(root, "passwd"), """
            glsitealias:*:1:2:1
            gluser:*:1:2:
            glrippy:*:1:2:6
            """);

        var groupLines = new List<string>();
        foreach (var group in groups)
        {
            groupLines.Add($"{group}:0:0:");
        }

        File.WriteAllText(
            Path.Combine(root, "group"),
            string.Join(Environment.NewLine, groupLines));

        File.WriteAllText(Path.Combine(root, "glftpd.pre"), """
            MP3-LEGACY ALPHA-ONE USERS 1700020100
            UNMAPPED UNMAPPED-ONE USERS 1700020110
            XVID XVID-ONE USERS 1700020120
            """);

        File.WriteAllText(Path.Combine(root, "ftp-data/misc/dupefile.txt"), """
            MP3-LEGACY|ALPHA-ONE|USERS|1700020200|1048576
            UNMAPPED|UNMAPPED-ONE|USERS|1700020210|2048576
            XVID|XVID-ONE|USERS|1700020220|3072000
            """);

        File.WriteAllText(Path.Combine(root, "glftpd.nuke"), """
            NUKE MP3-LEGACY ALPHA-ONE 2 bad staff 1700020300
            NUKE UNMAPPED UNMAPPED-ONE 3 bad staff2 1700020310
            NUKE XVID XVID-ONE 4 bad staff3 1700020320
            """);

        return root;
    }

    private static IEnumerable<(string Name, bool IsSiteop, bool IsAdmin, bool IsNoRatio)> ExpectedIoUsers()
        => new[]
        {
            ("iomanager", true, true, false),
            ("iopacker", true, false, false),
            ("ioni", false, false, true),
            ("ioupload", false, true, false)
        };

    private static IEnumerable<string> ExpectedIoGroups()
        => new[]
        {
            "USERS",
            "RIPPERS",
            "SCRIPTERS"
        };

    private static IEnumerable<(string Section, string Release)> ExpectedIoPres()
        => new[]
        {
            ("MP3", "IO-ALPHA-ONE"),
            ("XVID", "IO-XVID-TWO")
        };

    private static IEnumerable<(string Section, string Release)> ExpectedIoDupes()
        => new[]
        {
            ("MP3", "IO-ALPHA-ONE"),
            ("XVID", "IO-XVID-TWO")
        };

    private static IEnumerable<(string VirtualPath, string Reason)> ExpectedIoNukes()
        => new[]
        {
            ("/MP3/IO-ALPHA-ONE", "bad staff"),
            ("/XVID/IO-XVID-TWO", "corrupt staff2")
        };

    private static IEnumerable<(string Name, bool IsSiteop, bool IsAdmin, bool IsNoRatio)> ExpectedGlUsers()
        => new[]
        {
            ("gladmin", true, true, true),
            ("glsite", true, false, false),
            ("glnoratio", false, true, true),
            ("gluser", false, false, false)
        };

    private static IEnumerable<string> ExpectedGlGroups()
        => new[]
        {
            "DEFAULT",
            "RUSERS",
            "RAPTORS"
        };

    private static IEnumerable<(string Section, string Release)> ExpectedGlPres()
        => new[]
        {
            ("MP3", "GL-ALPHA-ONE"),
            ("0DAY", "GL-BETA-TWO")
        };

    private static IEnumerable<(string Section, string Release)> ExpectedGlDupes()
        => new[]
        {
            ("MP3", "GL-ALPHA-ONE"),
            ("0DAY", "GL-BETA-TWO")
        };

    private static IEnumerable<(string VirtualPath, string Reason)> ExpectedGlNukes()
        => new[]
        {
            ("/MP3/GL-ALPHA-ONE", "bad"),
            ("/0DAY/GL-BETA-TWO", "severe reason")
        };

    private static async Task<AmFtpdRuntimeConfig> CreateAliasAwareMigrationRuntimeAsync(
        string baseConfigPath)
    {
        var aliasConfigPath = await CreateAliasAwareMigrationConfigAsync(baseConfigPath);
        return await AmFtpdConfigLoader.LoadAsync(
            aliasConfigPath,
            QuickLogFactory.CreateTestLogger(out _));
    }

    private static async Task<string> CreateAliasAwareMigrationConfigAsync(string baseConfigPath)
    {
        var baseConfig = await File.ReadAllTextAsync(baseConfigPath);
        var configNode = JsonNode.Parse(baseConfig)?.AsObject()
                         ?? throw new InvalidOperationException("Invalid base config JSON.");

        var storage = configNode["Storage"]?.AsObject()
                      ?? throw new InvalidOperationException("Config file is missing Storage section.");

        var sourceSectionsPath = storage["SectionsPath"]?.GetValue<string>()
                               ?? throw new InvalidOperationException("Config file is missing Storage.SectionsPath.");

        var configDir = Path.GetDirectoryName(baseConfigPath)
                        ?? throw new InvalidOperationException("Cannot determine base config directory.");
        var aliasMigrationRoot = Path.Combine(
            configDir,
            $"amftpd-migration-alias-{Guid.NewGuid():N}");

        Directory.CreateDirectory(aliasMigrationRoot);

        var aliasSectionsPath = Path.Combine(
            configDir,
            $"amftpd-sections-alias-migration-{Guid.NewGuid():N}.json");

        await CreateAliasAwareSectionsAsync(
            sourceSectionsPath,
            aliasSectionsPath);

        storage["SectionsPath"] = aliasSectionsPath;
        storage["BaseDirectory"] = aliasMigrationRoot;
        storage["UsersDbPath"] = Path.Combine(aliasMigrationRoot, "users.json");
        storage["GroupsDbPath"] = Path.Combine(aliasMigrationRoot, "groups.db");
        storage["SectionsDbPath"] = Path.Combine(aliasMigrationRoot, "sections.db");
        storage["DupeStoreBackend"] = "file";
        storage["DupeStorePath"] = Path.Combine(aliasMigrationRoot, "dupes.json");
        storage["DupeContentDbPath"] = Path.Combine(aliasMigrationRoot, "dupe-contents.db");

        var aliasConfigPath = Path.Combine(
            configDir,
            $"amftpd-runtime-alias-migration-{Guid.NewGuid():N}.json");

        await File.WriteAllTextAsync(
            aliasConfigPath,
            configNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return aliasConfigPath;
    }

    private static async Task CreateAliasAwareSectionsAsync(string sourceSectionsPath, string aliasSectionsPath)
    {
        var sectionsText = await File.ReadAllTextAsync(sourceSectionsPath);
        var sections = JsonNode.Parse(sectionsText)?.AsArray()
                       ?? throw new InvalidOperationException("Invalid sections JSON.");

        foreach (var section in sections.OfType<JsonObject>())
        {
            var name = section["Name"]?.GetValue<string>();
            if (string.Equals(name, "MP3", StringComparison.OrdinalIgnoreCase))
            {
                section["Aliases"] = new JsonArray("MP3-LEGACY");
            }
        }

        var hasXvid = sections.OfType<JsonObject>()
            .Any(s => string.Equals(
                s["Name"]?.GetValue<string>(),
                "XVID",
                StringComparison.OrdinalIgnoreCase));

        if (!hasXvid)
        {
            sections.Add(new JsonObject
            {
                ["Name"] = "XVID",
                ["RatioRuleName"] = "STANDARD",
                ["DirectoryRules"] = new JsonArray()
            });
        }

        await File.WriteAllTextAsync(
            aliasSectionsPath,
            sections.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void AssertAllPreEntriesImported(
        amFTPd.Core.Pre.PreRegistry preRegistry,
        IEnumerable<(string Section, string Release)> expected)
    {
        foreach (var item in expected)
        {
            var release = preRegistry.FindByReleaseName(item.Release);
            Assert.NotNull(release);
            Assert.Equal(item.Section, release!.Section);
            Assert.Equal($"/{item.Section}/{item.Release}", release.VirtualPath);
        }
    }

    private static void AssertAllDupeEntriesImported(
        amFTPd.Core.Dupe.IDupeStore? dupeStore,
        IEnumerable<(string Section, string Release)> expected)
    {
        Assert.NotNull(dupeStore);
        foreach (var item in expected)
        {
            var dupe = dupeStore!.Find(item.Section, item.Release);
            Assert.NotNull(dupe);
        }
    }

    private static void AssertAllNukesImported(
        amFTPd.Core.Zipscript.ZipscriptEngine zipscript,
        IEnumerable<(string VirtualPath, string Reason)> expected)
    {
        foreach (var item in expected)
        {
            var status = zipscript.GetStatus(item.VirtualPath);
            Assert.NotNull(status);
            Assert.True(status!.IsNuked);
            Assert.Equal(item.Reason, status.NukeReason);
        }
    }
}
