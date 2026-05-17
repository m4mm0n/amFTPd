using amFTPd.Core.Pre;

namespace amFTPd.Tests;

public sealed class PreRegistryTests
{
    [Fact]
    public void TryRemove_AcceptsVirtualPathOrReleaseNameAndRemovesEntry()
    {
        var registry = new PreRegistry();
        var virtualPath = "/0DAY/TEST.REMOVE.ZLS";
        var releaseName = "TEST.REMOVE.ZLS";

        var byPath = new PreEntry(
            "0DAY",
            releaseName,
            virtualPath,
            "testuser",
            DateTimeOffset.UtcNow);

        registry.TryAdd(byPath);

        Assert.True(registry.TryRemove(virtualPath), "Removal by virtual path should succeed.");
        Assert.False(registry.TryRemove(virtualPath), "Removing an already removed pre should fail.");

        registry.TryAdd(byPath);
        Assert.True(registry.TryRemove(releaseName), "Removal by release name should succeed.");
        Assert.False(registry.TryRemove(releaseName), "Removing an already removed pre should fail.");
    }

    [Fact]
    public void TryRemove_IgnoresEmptyReleaseName()
    {
        var registry = new PreRegistry();

        Assert.False(registry.TryRemove(string.Empty));
        Assert.False(registry.TryRemove("   "));
    }
}
