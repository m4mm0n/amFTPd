using System.Threading.Tasks;
using FluentFTP;
using Xunit;

namespace amFTPd.Tests;

public class VfsTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public VfsTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanListRootDirectory_Shows0DAYAndMP3()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        var items = await client.GetListing("/");

        Assert.Contains(items, i => i.Name == "0DAY" && i.Type == FtpObjectType.Directory);
        Assert.Contains(items, i => i.Name == "MP3" && i.Type == FtpObjectType.Directory);
    }

    [Fact]
    public async Task CanNavigateTo_0DAY()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        await client.SetWorkingDirectory("/0DAY");
        var pwd = await client.GetWorkingDirectory();

        Assert.Equal("/0DAY", pwd);
    }

    [Fact]
    public async Task GAdmin_CanCreateDirectory_In0DAY()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);

        var dirName = "/0DAY/Some.Release-GADMIN";
        var created = await client.CreateDirectory(dirName);

        Assert.True(created, $"Failed to create directory {dirName}");

        var exists = await client.DirectoryExists(dirName);
        Assert.True(exists, "Directory was reported created but does not exist on VFS");
    }
}
