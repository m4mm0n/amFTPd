using FluentFTP;

namespace amFTPd.Tests;

public sealed class AuthorizationTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public AuthorizationTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("SITE NUKE /0DAY never.allowed")]
    [InlineData("SITE UNNUKE /0DAY/Some.Release.NUKED")]
    [InlineData("SITE GIVECRED testuser 100")]
    [InlineData("SITE TAKECRED testuser 100")]
    [InlineData("SITE ADDUSER eviluser password users /")]
    [InlineData("SITE DELUSER admin")]
    [InlineData("SITE CHPASS admin password")]
    [InlineData("SITE SETFLAGS testuser +1")]
    [InlineData("SITE GROUPADD EVILGROUP")]
    [InlineData("SITE NORATIO admin ON")]
    [InlineData("SITE LOG EVERYTHING")]
    public async Task NormalUser_CannotExecutePrivilegedMutationCommands(string command)
    {
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        var reply = await client.Execute(command);

        Assert.False(reply.Success, $"{command} unexpectedly succeeded: {reply.Code} {reply.Message}");
    }

    [Fact]
    public async Task NormalUser_CannotQueryOtherUsersCredits()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        var reply = await client.Execute($"SITE CREDITS {_fixture.GAdminUser}");

        Assert.False(reply.Success, $"SITE CREDITS for another user unexpectedly succeeded: {reply.Code} {reply.Message}");
    }

    [Fact]
    public async Task NormalUser_CanQueryOwnCredits()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        var reply = await client.Execute("SITE CREDITS");

        Assert.True(reply.Success, $"SITE CREDITS failed for own account: {reply.Code} {reply.Message}");
        Assert.Contains("CREDITSKB=", reply.Message, StringComparison.OrdinalIgnoreCase);
    }
}
