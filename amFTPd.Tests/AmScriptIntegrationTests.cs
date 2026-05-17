using System.Threading.Tasks;
using amFTPd.Logging;
using FluentFTP;
using FluentFTP.Exceptions;
using Xunit;

namespace amFTPd.Tests;

[Collection("AMScriptTests")]
public class AmScriptIntegrationTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public AmScriptIntegrationTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserRules_DenyCwd_ShouldBlockAccess()
    {
        var rulesDir = Path.Combine(Path.GetDirectoryName(_fixture.ConfigPath)!, "config", "rules");
        var userRulesPath = Path.Combine(rulesDir, "user-rules.msl");

        await File.WriteAllTextAsync(userRulesPath, "if ($event == \"CWD\") return deny \"NO-CWD-FOR-YOU\";");
        await Task.Delay(2000);

        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        var reply = await client.Execute("CWD /MP3");

        Assert.False(reply.Success);
        Assert.Equal("550", reply.Code);
        Assert.Contains("NO-CWD-FOR-YOU", reply.Message);

        await File.WriteAllTextAsync(userRulesPath, "");
        await Task.Delay(2000);

        var reply2 = await client.Execute("CWD /MP3");
        Assert.True(reply2.Success);
    }

    [Fact]
    public async Task IsAdmin_Variable_InScript_ShouldWork()
    {
        var rulesDir = Path.Combine(Path.GetDirectoryName(_fixture.ConfigPath)!, "config", "rules");
        var userRulesPath = Path.Combine(rulesDir, "user-rules.msl");

        await File.WriteAllTextAsync(userRulesPath, "if ($is_admin == \"false\" && $event == \"MKD\") return deny \"ONLY-ADMINS-CAN-MKD\";");
        await Task.Delay(2000);

        // 1. Normal user should be denied
        using (var clientUser = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass))
        {
            var reply = await clientUser.Execute("MKD /0DAY/Fail.Release-GRP");
            Assert.False(reply.Success);
            Assert.Equal("550", reply.Code);
            Assert.Contains("ONLY-ADMINS-CAN-MKD", reply.Message);
        }

        // 2. Admin should be allowed
        using (var clientAdmin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            var replyAdmin = await clientAdmin.Execute("MKD /0DAY/Admin.Release-GRP");
            Assert.True(replyAdmin.Success);
        }

        // Cleanup
        await File.WriteAllTextAsync(userRulesPath, "");
        await Task.Delay(1000);
    }
}
