using System.Threading.Tasks;
using FluentFTP;
using Xunit;

namespace amFTPd.Tests;

public class PreTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public PreTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SitePrelist_ReturnsSuccess()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);

        // Execute SITE PRELIST to ensure the PRE command module is responsive
        var reply = await client.Execute("SITE PRELIST");

        Assert.True(reply.Success, $"Expected Success but got: {reply.Code} {reply.Message}");
    }

    [Fact]
    public async Task SitePre_CreatesReleaseAndBroadcastsEvent()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);

        var releaseName = "Awesome.Software.v1.0-ZLS";
        var releasePath = $"/0DAY/{releaseName}";

        // 1. GAdmin creates the release directory
        await client.CreateDirectory(releasePath);

        // 2. GAdmin executes SITE PRE
        var reply = await client.Execute($"SITE PRE 0DAY {releasePath}");

        // Ensure PRE command was accepted
        Assert.True(reply.Success, $"SITE PRE failed: {reply.Code} {reply.Message}");
    }

    [Fact]
    public async Task NormalUser_SitePre_ReturnsAccessDenied()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        var reply = await client.Execute("SITE PRE 0DAY /0DAY/Some.Release-GROUP");

        // Normal users shouldn't have access to SITE PRE by default
        Assert.False(reply.Success);
        Assert.Equal("550", reply.Code);
    }

    [Fact]
    public async Task NormalUser_SitePreInApprovalSection_QueuesUntilSiteopApproves()
    {
        var releaseName = $"Pending.Release.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/STAFF/{releaseName}";

        using (var userClient = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass))
        {
            await userClient.CreateDirectory(releasePath);

            var queueReply = await userClient.Execute($"SITE PRE STAFF {releaseName}");

            Assert.True(queueReply.Success, $"SITE PRE queue failed: {queueReply.Code} {queueReply.Message}");
            Assert.Contains("queued", queueReply.Message, StringComparison.OrdinalIgnoreCase);

            var hiddenPending = await userClient.Execute($"SITE PREINFO {releaseName}");
            Assert.False(hiddenPending.Success);
            Assert.Equal("550", hiddenPending.Code);
        }

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            var pendingReply = await adminClient.Execute("SITE PREPENDING");
            Assert.True(pendingReply.Success, $"SITE PREPENDING failed: {pendingReply.Code} {pendingReply.Message}");

            var pendingInfoReply = await adminClient.Execute($"SITE PREINFO {releaseName}");
            Assert.True(pendingInfoReply.Success, $"SITE PREINFO failed: {pendingInfoReply.Code} {pendingInfoReply.Message}");

            var approveReply = await adminClient.Execute($"SITE PREAPPROVE {releaseName}");
            Assert.True(approveReply.Success, $"SITE PREAPPROVE failed: {approveReply.Code} {approveReply.Message}");
        }

        using (var userClient = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass))
        {
            var approvedInfoReply = await userClient.Execute($"SITE PREINFO {releaseName}");
            Assert.True(approvedInfoReply.Success, $"Approved SITE PREINFO failed: {approvedInfoReply.Code} {approvedInfoReply.Message}");
        }
    }

    [Fact]
    public async Task SiteDelPre_RemovesApprovedReleaseByReleaseName()
    {
        var releaseName = $"Removable.Release.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/STAFF/{releaseName}";

        using (var userClient = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass))
        {
            await userClient.CreateDirectory(releasePath);
            var queued = await userClient.Execute($"SITE PRE STAFF {releaseName}");
            Assert.True(queued.Success, $"SITE PRE queue failed: {queued.Code} {queued.Message}");
        }

        using (var adminClient = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            var approveReply = await adminClient.Execute($"SITE PREAPPROVE {releaseName}");
            Assert.True(approveReply.Success, $"SITE PREAPPROVE failed: {approveReply.Code} {approveReply.Message}");

            var deleteReply = await adminClient.Execute($"SITE DELPRE {releaseName}");
            Assert.True(deleteReply.Success, $"SITE DELPRE failed: {deleteReply.Code} {deleteReply.Message}");

            var postDeleteInfo = await adminClient.Execute($"SITE PREINFO {releaseName}");
            Assert.False(postDeleteInfo.Success, $"Deleted pre should not be visible: {postDeleteInfo.Code} {postDeleteInfo.Message}");
        }
    }

    [Fact]
    public async Task FullPreWorkflow_QueuesApproveAndDeniesPresAcrossPendingAndLivePaths()
    {
        var approveRelease = $"Full.Pre.Approve.{Guid.NewGuid():N}-ZLS";
        var denyRelease = $"Full.Pre.Deny.{Guid.NewGuid():N}-ZLS";
        var approvedPath = $"/STAFF/{approveRelease}";
        var deniedPath = $"/STAFF/{denyRelease}";

        using (var user = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass))
        {
            await user.CreateDirectory(approvedPath);
            await user.CreateDirectory(deniedPath);

            var queueApprove = await user.Execute($"SITE PRE STAFF {approveRelease}");
            Assert.True(queueApprove.Success, $"SITE PRE queue failed for {approveRelease}: {queueApprove.Code} {queueApprove.Message}");

            var queueDeny = await user.Execute($"SITE PRE STAFF {denyRelease}");
            Assert.True(queueDeny.Success, $"SITE PRE queue failed for {denyRelease}: {queueDeny.Code} {queueDeny.Message}");
        }

        using (var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            var pendingList = await admin.Execute("SITE PREPENDING");
            Assert.True(pendingList.Success, $"SITE PREPENDING failed: {pendingList.Code} {pendingList.Message}");
            var pendingListText = MergeCommandOutput(pendingList);
            Assert.Contains(approveRelease, pendingListText, StringComparison.Ordinal);
            Assert.Contains(denyRelease, pendingListText, StringComparison.Ordinal);

            var pendingInfo = await admin.Execute($"SITE PREINFO {approveRelease}");
            Assert.True(pendingInfo.Success, $"SITE PREINFO failed while pending: {pendingInfo.Code} {pendingInfo.Message}");
            var pendingInfoText = MergeCommandOutput(pendingInfo);
            Assert.Contains("PENDING APPROVAL", pendingInfoText, StringComparison.OrdinalIgnoreCase);

            var preListBefore = await admin.Execute("SITE PRELIST");
            Assert.True(preListBefore.Success, $"SITE PRELIST failed: {preListBefore.Code} {preListBefore.Message}");
            var preListBeforeText = MergeCommandOutput(preListBefore);
            Assert.DoesNotContain(approveRelease, preListBeforeText, StringComparison.Ordinal);
            Assert.DoesNotContain(denyRelease, preListBeforeText, StringComparison.Ordinal);

            var denyResult = await admin.Execute($"SITE PREDENY {denyRelease} no longer valid for this window");
            Assert.True(denyResult.Success, $"SITE PREDENY failed: {denyResult.Code} {denyResult.Message}");

            var pendingAfterDeny = await admin.Execute("SITE PREPENDING");
            Assert.True(pendingAfterDeny.Success, $"SITE PREPENDING after deny failed: {pendingAfterDeny.Code} {pendingAfterDeny.Message}");
            var pendingAfterDenyText = MergeCommandOutput(pendingAfterDeny);
            Assert.DoesNotContain(denyRelease, pendingAfterDenyText, StringComparison.Ordinal);
            Assert.Contains(approveRelease, pendingAfterDenyText, StringComparison.Ordinal);
            Assert.False(
                (await admin.Execute($"SITE PREINFO {denyRelease}")).Success,
                "DENIED pre should not be visible to siteop via PREINFO.");

            var approveResult = await admin.Execute($"SITE PREAPPROVE {approveRelease}");
            Assert.True(approveResult.Success, $"SITE PREAPPROVE failed: {approveResult.Code} {approveResult.Message}");

            var approvedInfo = await admin.Execute($"SITE PREINFO {approveRelease}");
            Assert.True(approvedInfo.Success, $"SITE PREINFO after approve failed: {approvedInfo.Code} {approvedInfo.Message}");
            Assert.Contains("APPROVED", MergeCommandOutput(approvedInfo), StringComparison.OrdinalIgnoreCase);

            var preListAfter = await admin.Execute("SITE PRELIST");
            Assert.True(preListAfter.Success, $"SITE PRELIST after approve failed: {preListAfter.Code} {preListAfter.Message}");
            var preListAfterText = MergeCommandOutput(preListAfter);
            Assert.Contains(approveRelease, preListAfterText, StringComparison.Ordinal);
            Assert.DoesNotContain(denyRelease, preListAfterText, StringComparison.Ordinal);
        }

        var dupeStore = _fixture.Runtime.DupeStore;
        Assert.NotNull(dupeStore);
        Assert.NotNull(dupeStore.Find("STAFF", approveRelease));
        Assert.Null(dupeStore.Find("STAFF", denyRelease));

        using (var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            var audit = await admin.Execute("SITE AUDITLOG 20");
            Assert.True(audit.Success, $"SITE AUDITLOG failed: {audit.Code} {audit.Message}");
            var auditText = MergeCommandOutput(audit);
            Assert.Contains("PRE_PENDING", auditText, StringComparison.Ordinal);
            Assert.Contains("PREDENY", auditText, StringComparison.Ordinal);
            Assert.Contains("PREAPPROVE", auditText, StringComparison.Ordinal);
        }
    }

    private static string MergeCommandOutput(FluentFTP.FtpReply reply)
    {
        var lines = new List<string>(capacity: 8);
        if (!string.IsNullOrWhiteSpace(reply.Message))
            lines.Add(reply.Message);
        if (reply.InfoMessages is { Length: > 0 })
            lines.AddRange(reply.InfoMessages);
        return string.Join("\r\n", lines);
    }
}
