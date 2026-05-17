using System.Threading.Tasks;
using FluentFTP;
using Xunit;
using Xunit.Abstractions;

namespace amFTPd.Tests;

[Collection("AMScriptTests")]
public class TclIntegrationTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TclIntegrationTests(FtpTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task SiteTclCommand_ExecutesAndReturnsOutput()
    {
        // 1. Setup a mock TCL script
        var configDir = Path.Combine(Path.GetDirectoryName(_fixture.ConfigPath)!, "config");
        var scriptsDir = Path.Combine(configDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        var scriptPath = Path.Combine(scriptsDir, "test.tcl");
        await File.WriteAllTextAsync(scriptPath, "puts \"Hello $USER\"");

        // 2. Update config
        var runtime = _fixture.GetType().GetField("_server", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_fixture)!
            .GetType().GetProperty("Runtime")!.GetValue(_fixture.GetType().GetField("_server", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_fixture))!;

        var tclCfg = new amFTPd.Config.Ftpd.TclConfig
        {
            ScriptsPath = scriptsDir,
            SiteCommands = new Dictionary<string, string> { { "TCLTEST", "test.tcl" } }
        };

        runtime.GetType().GetProperty("Tcl")!.SetValue(runtime, tclCfg);
        runtime.GetType().GetProperty("TclRunner")!.SetValue(runtime, new amFTPd.Scripting.Tcl.GlftpdTclRunner(scriptsDir));

        // 3. Connect and execute
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);
        var reply = await client.Execute("SITE TCLTEST");

        // Log the full reply for debugging
        _output.WriteLine($"Reply Code: {reply.Code}");
        _output.WriteLine($"Reply Message: {reply.Message}");
        _output.WriteLine($"Reply InfoMessages Count: {reply.InfoMessages?.Length ?? 0}");
        if (reply.InfoMessages != null)
        {
            foreach (var msg in reply.InfoMessages) _output.WriteLine($"Info: {msg}");
        }

        // Assert
        Assert.True(reply.Success);

        // If FluentFTP's Execute combines them:
        var fullOutput = reply.Message;

        // Or if it's in InfoMessages:
        if (reply.InfoMessages != null && reply.InfoMessages.Length > 0)
        {
            fullOutput = string.Join("\n", reply.InfoMessages);
        }

        Assert.Contains($"Hello {_fixture.NormalUser}", fullOutput);
    }
}
