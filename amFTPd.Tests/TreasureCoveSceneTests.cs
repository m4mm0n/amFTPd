using System.Text;
using FluentFTP;

namespace amFTPd.Tests;

public sealed class TreasureCoveSceneTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public TreasureCoveSceneTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TreasureCove_GroupsCanPreAcrossAllConfiguredSections()
    {
        var approvedReleases = new List<string>();

        foreach (var account in _fixture.TreasureCoveAccounts)
        {
            var releaseName = $"{account.GroupName}.Treasure.Cove.{Guid.NewGuid():N}-ZLS";
            var releasePath = $"/{account.SectionName}/{releaseName}";

            using var client = await _fixture.CreateClientAsync(account.UserName, account.Password);
            await client.CreateDirectory(releasePath);

            var queueReply = await client.Execute($"SITE PRE {account.SectionName} {releaseName}");
            Assert.True(queueReply.Success, $"SITE PRE failed for {account.GroupName}/{account.SectionName}: {queueReply.Code} {queueReply.Message}");

            approvedReleases.Add(releaseName);
        }

        using var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);
        foreach (var releaseName in approvedReleases)
        {
            var pendingInfo = await admin.Execute($"SITE PREINFO {releaseName}");
            Assert.True(pendingInfo.Success, $"SITE PREINFO failed for pending {releaseName}: {pendingInfo.Code} {pendingInfo.Message}");

            var approve = await admin.Execute($"SITE PREAPPROVE {releaseName}");
            Assert.True(approve.Success, $"SITE PREAPPROVE failed for {releaseName}: {approve.Code} {approve.Message}");

            var approvedInfo = await admin.Execute($"SITE PREINFO {releaseName}");
            Assert.True(approvedInfo.Success, $"SITE PREINFO failed for approved {releaseName}: {approvedInfo.Code} {approvedInfo.Message}");
        }
    }

    [Fact]
    public async Task Admin_CanCreateInspectAndDeleteEmptySceneGroup()
    {
        var groupName = $"TCVTEST{Guid.NewGuid():N}"[..16];

        using var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);

        var add = await admin.Execute($"SITE GROUPADD {groupName} The Treasure Cove test group");
        Assert.True(add.Success, $"SITE GROUPADD failed: {add.Code} {add.Message}");

        var info = await admin.Execute($"SITE GROUPINFO {groupName}");
        Assert.True(info.Success, $"SITE GROUPINFO failed: {info.Code} {info.Message}");

        var del = await admin.Execute($"SITE GROUPDEL {groupName}");
        Assert.True(del.Success, $"SITE GROUPDEL failed: {del.Code} {del.Message}");

        var infoAfterDelete = await admin.Execute($"SITE GROUPINFO {groupName}");
        Assert.False(infoAfterDelete.Success, "Deleted group should not be visible through SITE GROUPINFO.");
    }

    [Fact]
    public async Task Rscheck_ReportsUploadedSfvReleaseStatus()
    {
        var releaseName = $"RSCHECK.Treasure.Cove.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/TCV0DAY/{releaseName}";
        var payload = Encoding.ASCII.GetBytes("treasure-cove-release-payload");
        var crc = amFTPd.Utils.Cryptography.Crc32.Compute(payload);
        var sfv = Encoding.ASCII.GetBytes($"file001.zip {crc:X8}\r\n");

        using var client = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);
        await client.CreateDirectory(releasePath);
        await client.UploadBytes(sfv, $"{releasePath}/release.sfv", FtpRemoteExists.Overwrite);
        await client.UploadBytes(payload, $"{releasePath}/file001.zip", FtpRemoteExists.Overwrite);

        var rscheck = await client.Execute($"SITE RSCHECK {releasePath}");
        Assert.True(rscheck.Success, $"SITE RSCHECK failed: {rscheck.Code} {rscheck.Message}");
    }
}
