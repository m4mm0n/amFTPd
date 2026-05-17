using System.Text;
using amFTPd.Config.Ftpd;
using amFTPd.Security;
using FluentFTP;

namespace amFTPd.Tests;

public sealed class CreditWorkflowTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public CreditWorkflowTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Upload_AwardsCreditsToUploader()
    {
        var releaseName = $"Credit.Workflow.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/0DAY/{releaseName}";
        var payload = Encoding.ASCII.GetBytes(new string('C', 4096));

        long initialCredits;
        long afterUploadCredits;

        using (var userClient = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass))
        {
            initialCredits = await GetOwnCreditsAsync(userClient);

            await userClient.CreateDirectory(releasePath);
            await userClient.UploadBytes(payload, $"{releasePath}/file001.zip", FtpRemoteExists.Overwrite);

            afterUploadCredits = await GetOwnCreditsAsync(userClient);
        }

        Assert.True(afterUploadCredits > initialCredits, $"Expected upload to award credits above {initialCredits}, got {afterUploadCredits}.");
    }

    [Fact]
    public async Task NoRatioUser_CanDownloadWithoutCreditDeduction()
    {
        var userName = $"noratio{Guid.NewGuid():N}"[..20];
        const string password = "noratiopass";
        var releaseName = $"Credit.NoRatio.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/0DAY/{releaseName}";
        var remoteFile = $"{releasePath}/file001.zip";
        var payload = Encoding.ASCII.GetBytes(new string('F', 4096));

        var noRatioUser = new FtpUser
        {
            UserName = userName,
            PasswordHash = PasswordHasher.HashPassword(password),
            GroupName = "users",
            CreditsKb = 0,
            IsNoRatio = true,
            MaxCommandsPerMinuteOverride = 10_000
        };

        if (!_fixture.Runtime.UserStore.TryAddUser(noRatioUser, out _))
            _fixture.Runtime.UserStore.TryUpdateUser(noRatioUser, out _);

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            await adminClient.CreateDirectory(releasePath);
            await adminClient.UploadBytes(payload, remoteFile, FtpRemoteExists.Overwrite);
        }

        using (var noRatioClient = await _fixture.CreateClientAsync(userName, password))
        {
            var beforeDownload = await GetOwnCreditsAsync(noRatioClient);
            Assert.Equal(0, beforeDownload);

            var downloaded = await noRatioClient.DownloadBytes(remoteFile, 0L);

            Assert.Equal(payload, downloaded);
            Assert.Equal(beforeDownload, await GetOwnCreditsAsync(noRatioClient));
        }
    }

    private static async Task<long> GetOwnCreditsAsync(AsyncFtpClient client)
    {
        var reply = await client.Execute("SITE CREDITS");

        Assert.True(reply.Success, $"SITE CREDITS failed: {reply.Code} {reply.Message}");

        var marker = "CREDITSKB=";
        var index = reply.Message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        Assert.True(index >= 0, $"SITE CREDITS did not include {marker}: {reply.Message}");

        var value = reply.Message[(index + marker.Length)..].Trim();
        Assert.True(long.TryParse(value, out var credits), $"Could not parse credits from: {reply.Message}");

        return credits;
    }
}
