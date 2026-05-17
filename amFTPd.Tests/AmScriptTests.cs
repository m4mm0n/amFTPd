using amFTPd.Scripting;
using Xunit;

namespace amFTPd.Tests;

public class AmScriptTests : IDisposable
{
    private readonly string _testScriptPath;

    public AmScriptTests()
    {
        _testScriptPath = Path.Combine(Path.GetTempPath(), $"test-script-{Guid.NewGuid()}.msl");
    }

    public void Dispose()
    {
        if (File.Exists(_testScriptPath)) File.Delete(_testScriptPath);
    }

    private AMScriptContext CreateDefaultContext(
        string user = "testuser",
        string group = "testgroup",
        string section = "DEFAULT",
        long bytes = 1024)
    {
        return new AMScriptContext(
            IsFxp: false,
            Section: section,
            FreeLeech: false,
            UserName: user,
            UserGroup: group,
            Bytes: bytes,
            Kb: bytes / 1024,
            CostDownload: bytes,
            EarnedUpload: bytes,
            VirtualPath: "/0DAY/Release",
            PhysicalPath: "C:\\site\\0DAY\\Release",
            Event: "UPLOAD",
            IsAdmin: false,
            IsSiteop: false,
            Arg: ""
        );
    }

    [Fact]
    public void SimpleAllowRule_ShouldSucceed()
    {
        File.WriteAllText(_testScriptPath, "if ($user.name == \"admin\") return allow;");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx = CreateDefaultContext(user: "admin");
        var result = engine.EvaluateUpload(ctx);

        Assert.Equal(AMRuleAction.Allow, result.Action);
    }

    [Fact]
    public void SimpleDenyRule_ShouldSucceed()
    {
        File.WriteAllText(_testScriptPath, "if ($user.group == \"leecher\") return deny \"No leeching allowed\";");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx = CreateDefaultContext(group: "leecher");
        var result = engine.EvaluateUpload(ctx);

        Assert.Equal(AMRuleAction.Deny, result.Action);
        Assert.Equal("No leeching allowed", result.DenyReason);
    }

    [Fact]
    public void MultipleConditions_WithAnd_ShouldSucceed()
    {
        File.WriteAllText(_testScriptPath, "if ($user.group == \"VIP\" && $section == \"0DAY\") return allow;");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx1 = CreateDefaultContext(group: "VIP", section: "0DAY");
        var result1 = engine.EvaluateUpload(ctx1);
        Assert.Equal(AMRuleAction.Allow, result1.Action);

        var ctx2 = CreateDefaultContext(group: "users", section: "0DAY");
        var result2 = engine.EvaluateUpload(ctx2);
        Assert.Equal(AMRuleAction.None, result2.Action);
    }

    [Fact]
    public void NumericAssignment_ShouldSucceed()
    {
        File.WriteAllText(_testScriptPath, "if ($section == \"FREE\") earned_upload *= 2;");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx = CreateDefaultContext(section: "FREE", bytes: 1000);
        // CostDownload = 1000, EarnedUpload = 1000
        var result = engine.EvaluateUpload(ctx);

        Assert.Equal(2000, result.EarnedUpload);
    }

    [Fact]
    public void HotReload_ShouldWork()
    {
        File.WriteAllText(_testScriptPath, "if ($user.name == \"test\") return deny;");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx = CreateDefaultContext(user: "test");
        var result1 = engine.EvaluateUpload(ctx);
        Assert.Equal(AMRuleAction.Deny, result1.Action);

        // Change the rule
        File.WriteAllText(_testScriptPath, "if ($user.name == \"test\") return allow;");

        // Wait for FileSystemWatcher and debounce
        Thread.Sleep(500);

        var result2 = engine.EvaluateUpload(ctx);
        Assert.Equal(AMRuleAction.Allow, result2.Action);
    }

    [Fact]
    public void CaseInsensitivity_ShouldWork()
    {
        File.WriteAllText(_testScriptPath, "IF ($user.name == \"admin\") RETURN ALLOW;");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx = CreateDefaultContext(user: "admin");
        var result = engine.EvaluateUpload(ctx);

        Assert.Equal(AMRuleAction.Allow, result.Action);
    }

    [Fact]
    public void IsAdmin_Variable_ShouldWork()
    {
        File.WriteAllText(_testScriptPath, "if ($is_admin) return deny \"Admins not allowed\";");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctxAdmin = CreateDefaultContext() with { IsAdmin = true };
        var ctxUser = CreateDefaultContext() with { IsAdmin = false };

        var result1 = engine.EvaluateUpload(ctxAdmin);
        Assert.Equal(AMRuleAction.Deny, result1.Action);

        var result2 = engine.EvaluateUpload(ctxUser);
        Assert.Equal(AMRuleAction.None, result2.Action);
    }

    [Fact]
    public void IsSiteop_Variable_ShouldWork()
    {
        File.WriteAllText(_testScriptPath, "if ($is_siteop) return deny \"Siteops not allowed\";");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctxSiteop = CreateDefaultContext() with { IsSiteop = true };
        var ctxUser = CreateDefaultContext() with { IsSiteop = false };

        var result1 = engine.EvaluateUpload(ctxSiteop);
        Assert.Equal(AMRuleAction.Deny, result1.Action);

        var result2 = engine.EvaluateUpload(ctxUser);
        Assert.Equal(AMRuleAction.None, result2.Action);
    }

    [Fact]
    public void CommandAndArg_Variables_ShouldWork()
    {
        File.WriteAllText(_testScriptPath, "if ($command == \"CWD\" && $arg == \"/SECRET\") return deny;");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx1 = CreateDefaultContext() with { Event = "CWD", Arg = "/SECRET" };
        var result1 = engine.EvaluateUpload(ctx1);
        Assert.Equal(AMRuleAction.Deny, result1.Action);

        var ctx2 = CreateDefaultContext() with { Event = "CWD", Arg = "/PUBLIC" };
        var result2 = engine.EvaluateUpload(ctx2);
        Assert.Equal(AMRuleAction.None, result2.Action);
    }

    [Fact]
    public void SiteCommand_Variable_ShouldWork()
    {
        File.WriteAllText(_testScriptPath, "if ($site.command == \"KICK\") return deny;");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx = CreateDefaultContext() with { Event = "SITE KICK", Arg = "user" };
        var result = engine.EvaluateUpload(ctx);
        Assert.Equal(AMRuleAction.Deny, result.Action);
    }

    [Fact]
    public void BooleanToken_ShouldWork()
    {
        File.WriteAllText(_testScriptPath, "if ($is_fxp) return deny \"No FXP allowed\";");
        using var engine = new AMScriptEngine(_testScriptPath);

        var ctx = CreateDefaultContext();
        var ctxFxp = ctx with { IsFxp = true };

        var result1 = engine.EvaluateUpload(ctx);
        Assert.Equal(AMRuleAction.None, result1.Action);

        var result2 = engine.EvaluateUpload(ctxFxp);
        Assert.Equal(AMRuleAction.Deny, result2.Action);
    }
}
