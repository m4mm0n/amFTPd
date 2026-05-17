using System.Globalization;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Import;
using amFTPd.Utils.Tools;

namespace amFTPd.Tests;

public sealed class GlIoImportTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public GlIoImportTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ImportWithSummary_AutoDetect_MapsSectionAlias_AndImports_AllMappedRecords()
    {
        var sourceRoot = BuildAliasedGlFixture();
        try
        {
            var sections = new SectionManager(new[]
            {
                new FtpSection
                {
                    Name = "MP3",
                    VirtualRoot = "/MP3",
                    Aliases = ["MP3-LEGACY"]
                },
                new FtpSection
                {
                    Name = "XVID",
                    VirtualRoot = "/XVID"
                },
                new FtpSection
                {
                    Name = "UNKNOWN",
                    VirtualRoot = "/UNKNOWN"
                }
            });

            var runtime = _fixture.Runtime;
            var result = await GlIoImport.ImportWithSummaryAsync(
                sourceRoot: sourceRoot,
                sections: sections,
                users: runtime.UserStore,
                groups: runtime.GroupStore!,
                dryRun: false,
                preRegistry: runtime.PreRegistry,
                dupeStore: runtime.DupeStore,
                zipscript: runtime.Zipscript);

            Assert.Equal(ImportFlavor.GlFtpd, result.Flavor);
            Assert.Equal(3, result.ParsedGroups);
            Assert.Equal(3, result.ImportedGroups);
            Assert.Equal(3, result.ParsedUsers);
            Assert.Equal(3, result.ImportedUsers);
            Assert.Equal(2, result.ParsedPres);
            Assert.Equal(1, result.ImportedPres);
            Assert.Equal(2, result.ParsedNukes);
            Assert.Equal(1, result.ImportedNukes);
            Assert.Equal(3, result.ParsedDupes);
            Assert.Equal(2, result.ImportedDupes);
            Assert.Contains("UNMAPPED", result.UnknownSections);
            Assert.Equal(1, result.UnknownSectionsCount);

            var aliasedUser = runtime.UserStore.FindUser("gladmin_alias");
            Assert.NotNull(aliasedUser);
            Assert.True(aliasedUser!.Disabled);
            Assert.True(aliasedUser.IsSiteop);
            Assert.False(aliasedUser.IsAdmin);

            var importedPre = runtime.PreRegistry.FindByReleaseName("ALIAS-RELEASE");
            Assert.NotNull(importedPre);
            Assert.Equal("MP3", importedPre!.Section);

            var importDupe = runtime.DupeStore!.Find("MP3", "ALIAS-RELEASE");
            Assert.NotNull(importDupe);
            Assert.Equal("RIPPERS", importDupe!.UploaderGroup);

            var importUnknownDupe = runtime.DupeStore.Find("UNMAPPED", "UNKNOWN-RELEASE");
            Assert.Null(importUnknownDupe);

            var importNuke = runtime.Zipscript!.GetStatus("/MP3/ALIAS-RELEASE");
            Assert.NotNull(importNuke);
            Assert.True(importNuke.IsNuked);
        }
        finally
        {
            try { Directory.Delete(sourceRoot, true); } catch { }
        }
    }

    private static string BuildAliasedGlFixture()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "amftpd-glio-import",
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "ftp-data", "misc"));

        File.WriteAllText(Path.Combine(root, "passwd"), """
            gladmin_alias:*:1:2:1
            glsiter:*:1:2:
            glother:*:1:2:6
            """);

        File.WriteAllLines(
            Path.Combine(root, "group"),
            [
                "DEFAULT:0:0:",
                "RIPPERS:0:0:",
                "SCRIPTERS:0:0:"
            ]);

        File.WriteAllText(Path.Combine(root, "glftpd.pre"), """
            MP3-LEGACY ALIAS-RELEASE RIPPERS 1700010000
            UNMAPPED UNKNOWN-RELEASE SCRIPTERS 1700010010
            """);

        File.WriteAllText(Path.Combine(root, "ftp-data/misc/dupefile.txt"), """
            MP3-LEGACY|ALIAS-RELEASE|RIPPERS|1700010100|1048576
            UNMAPPED|UNKNOWN-RELEASE|SCRIPTERS|1700010200|2048576
            XVID|XVID-RELEASE|RIPPERS|1700010300|4096
            """);

        File.WriteAllText(Path.Combine(root, "glftpd.nuke"), """
            NUKE MP3-LEGACY ALIAS-RELEASE 2 bad staff 1700010400
            NUKE UNMAPPED UNKNOWN-RELEASE 3 bad staff2 1700010410
            """);

        return root;
    }
}
