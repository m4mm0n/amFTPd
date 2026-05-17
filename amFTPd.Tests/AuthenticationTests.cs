using System.Threading.Tasks;
using FluentFTP;
using FluentFTP.Exceptions;
using Xunit;

namespace amFTPd.Tests;

public class AuthenticationTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public AuthenticationTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GAdmin_CanAuthenticate_Successfully()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);

        Assert.True(client.IsConnected);
        Assert.True(client.IsAuthenticated);
    }

    [Fact]
    public async Task NormalUser_CanAuthenticate_Successfully()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        Assert.True(client.IsConnected);
        Assert.True(client.IsAuthenticated);
    }

    [Fact]
    public async Task InvalidUser_ThrowsAuthenticationException()
    {
        var client = new AsyncFtpClient("127.0.0.1", "invaliduser", "wrongpass", _fixture.Port);

        await Assert.ThrowsAsync<FtpAuthenticationException>(async () =>
        {
            await client.AutoConnect();
        });
    }
}
