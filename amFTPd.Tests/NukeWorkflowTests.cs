using System.Text;
using FluentFTP;

namespace amFTPd.Tests;

public sealed class NukeWorkflowTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public NukeWorkflowTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task NukeAndUnnuke_DeductsAndRestoresUploaderCredits()
    {
        var releaseName = $"Nuke.Workflow.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/0DAY/{releaseName}";
        var nukedPath = $"{releasePath}.NUKED";
        var payload = Encoding.ASCII.GetBytes(new string('A', 4096));

        long initialCredits;
        long afterUploadCredits;
        long afterNukeCredits;
        long afterUnnukeCredits;

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            initialCredits = await GetCreditsAsync(adminClient, _fixture.NormalUser);
        }

        using (var userClient = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass))
        {
            await userClient.CreateDirectory(releasePath);
            await userClient.UploadBytes(payload, $"{releasePath}/file001.zip", FtpRemoteExists.Overwrite);
        }

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            afterUploadCredits = await GetCreditsAsync(adminClient, _fixture.NormalUser);
            Assert.True(afterUploadCredits >= initialCredits, $"Expected upload not to reduce credits below {initialCredits}, got {afterUploadCredits}.");

            var nukeReply = await adminClient.Execute($"SITE NUKE {releasePath} bad.release");
            Assert.True(nukeReply.Success, $"SITE NUKE failed: {nukeReply.Code} {nukeReply.Message}");

            afterNukeCredits = await GetCreditsAsync(adminClient, _fixture.NormalUser);
            Assert.True(
                afterNukeCredits < afterUploadCredits,
                $"Expected nuke to deduct some credits from {afterUploadCredits}, got {afterNukeCredits}.");
            Assert.False(await adminClient.DirectoryExists(releasePath));
            Assert.True(await adminClient.DirectoryExists(nukedPath));

            var unnukeReply = await adminClient.Execute($"SITE UNNUKE {nukedPath} fixed");
            Assert.True(unnukeReply.Success, $"SITE UNNUKE failed: {unnukeReply.Code} {unnukeReply.Message}");

            afterUnnukeCredits = await GetCreditsAsync(adminClient, _fixture.NormalUser);
            Assert.Equal(afterUploadCredits, afterUnnukeCredits);
            Assert.True(await adminClient.DirectoryExists(releasePath));
            Assert.False(await adminClient.DirectoryExists(nukedPath));
        }
    }

    private static async Task<long> GetCreditsAsync(AsyncFtpClient client, string userName)
    {
        var reply = await client.Execute($"SITE CREDITS {userName}");

        Assert.True(reply.Success, $"SITE CREDITS failed: {reply.Code} {reply.Message}");

        var marker = "CREDITSKB=";
        var index = reply.Message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        Assert.True(index >= 0, $"SITE CREDITS did not include {marker}: {reply.Message}");

        var value = reply.Message[(index + marker.Length)..].Trim();
        Assert.True(long.TryParse(value, out var credits), $"Could not parse credits from: {reply.Message}");

        return credits;
    }

    [Fact]
    public async Task NukeAndUnnuke_RecoversMultiUploaderCredits()
    {
        var releaseName = $"Nuke.Multi.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/0DAY/{releaseName}";
        var nukedPath = $"{releasePath}.NUKED";
        var payloadBytes = Encoding.ASCII.GetBytes(new string('A', 4096));
        var uploaders = _fixture.RaceAccounts.Take(2).ToArray();

        var baseline = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var postUpload = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            await adminClient.CreateDirectory(releasePath);

            foreach (var userName in _fixture.RaceAccounts.Select(a => a.UserName).Prepend(_fixture.NormalUser))
                baseline[userName] = await GetCreditsAsync(adminClient, userName);
        }

        await Task.WhenAll(uploaders.Select(async uploader =>
        {
            using var client = await _fixture.CreateClientAsync(uploader.UserName, uploader.Password);
            await client.UploadBytes(payloadBytes, $"{releasePath}/{uploader.UserName}.bin", FtpRemoteExists.Overwrite);
        }));

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            foreach (var userName in uploaders.Select(a => a.UserName))
                postUpload[userName] = await GetCreditsAsync(adminClient, userName);

            var nukeReply = await adminClient.Execute($"SITE NUKE {releasePath} multi-credits");
            Assert.True(nukeReply.Success, $"SITE NUKE failed: {nukeReply.Code} {nukeReply.Message}");

            foreach (var uploader in uploaders)
            {
                var afterNuke = await GetCreditsAsync(adminClient, uploader.UserName);
                Assert.True(
                    afterNuke < postUpload[uploader.UserName],
                    $"Expected nuke to deduct credits from {uploader.UserName}.");
            }

            var unnukeReply = await adminClient.Execute($"SITE UNNUKE {nukedPath} fixed");
            Assert.True(unnukeReply.Success, $"SITE UNNUKE failed: {unnukeReply.Code} {unnukeReply.Message}");

            foreach (var uploader in uploaders)
                Assert.Equal(postUpload[uploader.UserName], await GetCreditsAsync(adminClient, uploader.UserName));
        }
    }

    [Fact]
    public async Task NukeAndUnnuke_UpdatesDupeZipscriptAndRaceEvidence()
    {
        var releaseName = $"Nuke.State.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/0DAY/{releaseName}";
        var nukedPath = $"{releasePath}.NUKED";
        var payload = Encoding.ASCII.GetBytes(new string('N', 4096));

        using (var userClient = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass))
        {
            await userClient.CreateDirectory(releasePath);
            await userClient.UploadBytes(payload, $"{releasePath}/file001.zip", FtpRemoteExists.Overwrite);
        }

        Assert.True(
            _fixture.Runtime.RaceEngine.TryGetRace(releasePath, out var raceBeforeNuke),
            "Upload did not create race state before NUKE.");
        Assert.True(raceBeforeNuke.UserBytes.ContainsKey(_fixture.NormalUser));
        Assert.Equal(payload.Length, raceBeforeNuke.TotalBytes);

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            var nukeReply = await adminClient.Execute($"SITE NUKE {releasePath} bad.dupe.state");
            Assert.True(nukeReply.Success, $"SITE NUKE failed: {nukeReply.Code} {nukeReply.Message}");
        }

        var nukedDupe = FindDupeEntry(releaseName);
        Assert.NotNull(nukedDupe);
        Assert.True(nukedDupe.IsNuked);
        Assert.Equal("bad.dupe.state", nukedDupe.NukeReason);
        Assert.True(nukedDupe.NukeMultiplier > 0);
        Assert.True(
            nukedDupe.NukePenalties.ContainsKey(_fixture.NormalUser),
            "NUKE did not persist uploader penalty evidence in the dupe entry.");

        var nukedStatus = _fixture.Runtime.Zipscript?.GetStatus(releasePath);
        Assert.NotNull(nukedStatus);
        Assert.True(nukedStatus!.IsNuked);
        Assert.True(nukedStatus.WasNuked);
        Assert.Equal("bad.dupe.state", nukedStatus.NukeReason);

        Assert.True(
            _fixture.Runtime.RaceEngine.TryGetRace(releasePath, out var raceAfterNuke),
            "NUKE removed race evidence for the original release.");
        Assert.Equal(raceBeforeNuke.TotalBytes, raceAfterNuke.TotalBytes);
        Assert.Equal(raceBeforeNuke.FileCount, raceAfterNuke.FileCount);

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            var unnukeReply = await adminClient.Execute($"SITE UNNUKE {nukedPath} fixed.dupe.state");
            Assert.True(unnukeReply.Success, $"SITE UNNUKE failed: {unnukeReply.Code} {unnukeReply.Message}");
        }

        var unnukedDupe = FindDupeEntry(releaseName);
        Assert.NotNull(unnukedDupe);
        Assert.False(unnukedDupe.IsNuked);
        Assert.True(string.IsNullOrEmpty(unnukedDupe.NukeReason));
        Assert.Empty(unnukedDupe.NukePenalties);

        var unnukedStatus = _fixture.Runtime.Zipscript?.GetStatus(releasePath);
        Assert.NotNull(unnukedStatus);
        Assert.False(unnukedStatus!.IsNuked);
        Assert.True(unnukedStatus.WasNuked);
    }

    private amFTPd.Core.Dupe.DupeEntry? FindDupeEntry(string releaseName)
    {
        return _fixture.Runtime.DupeStore!.Find("0DAY", releaseName)
            ?? _fixture.Runtime.DupeStore.Find(string.Empty, releaseName)
            ?? _fixture.Runtime.DupeStore.Search(releaseName, sectionName: null, limit: 20)
                .FirstOrDefault(e => e.ReleaseName.Equals(releaseName, StringComparison.OrdinalIgnoreCase));
    }
}
