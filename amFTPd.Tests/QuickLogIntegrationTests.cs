using amFTPd.Logging;
using QuickLog;

namespace amFTPd.Tests;

public sealed class QuickLogIntegrationTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public QuickLogIntegrationTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void QuickLogFactory_DefaultMode_IsSomething()
    {
        using var logger = QuickLogFactory.CreateTestLogger(out _);

        Assert.Equal(QuickLogMode.Something, logger.Mode);
    }

    [Fact]
    public void QuickLogMode_Everything_WritesTraceDebugInfoWarnErrorCritical()
    {
        using var logger = QuickLogFactory.CreateTestLogger(QuickLogMode.Everything, out var memory);

        LogAllLevels(logger);

        var levels = memory.Snapshot().Select(e => e.LoggingType).ToArray();
        Assert.Contains(LogType.Trace, levels);
        Assert.Contains(LogType.Debug, levels);
        Assert.Contains(LogType.Info, levels);
        Assert.Contains(LogType.Warn, levels);
        Assert.Contains(LogType.Error, levels);
        Assert.Contains(LogType.Crit, levels);
    }

    [Fact]
    public void QuickLogMode_Something_WritesInfoWarnErrorCriticalOnly()
    {
        using var logger = QuickLogFactory.CreateTestLogger(QuickLogMode.Something, out var memory);

        LogAllLevels(logger);

        var levels = memory.Snapshot().Select(e => e.LoggingType).ToArray();
        Assert.DoesNotContain(LogType.Trace, levels);
        Assert.DoesNotContain(LogType.Debug, levels);
        Assert.Contains(LogType.Info, levels);
        Assert.Contains(LogType.Warn, levels);
        Assert.Contains(LogType.Error, levels);
        Assert.Contains(LogType.Crit, levels);
    }

    [Fact]
    public void QuickLogMode_Quiet_WritesErrorCriticalAndLifecycleOnly()
    {
        using var logger = QuickLogFactory.CreateTestLogger(QuickLogMode.Quiet, out var memory);

        LogAllLevels(logger);
        logger.Log(FtpLogLevel.Info, "[amFTPd] Server stopped.");

        var entries = memory.Snapshot();
        var levels = entries.Select(e => e.LoggingType).ToArray();
        Assert.DoesNotContain(LogType.Trace, levels);
        Assert.DoesNotContain(LogType.Debug, levels);
        Assert.DoesNotContain(LogType.Warn, levels);
        Assert.Contains(LogType.Error, levels);
        Assert.Contains(LogType.Crit, levels);
        Assert.Contains(
            entries,
            e => e.LoggingType == LogType.Info &&
                 (e.Message ?? string.Empty).Contains("Server stopped"));
    }

    [Fact]
    public async Task SiteLogCommand_AdminCanSwitchModes()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);

        var reply = await client.Execute("SITE LOG QUIET");

        Assert.True(reply.Success, $"SITE LOG QUIET failed: {reply.Code} {reply.Message}");
        Assert.Contains("LOG MODE QUIET", reply.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SiteLogCommand_NormalUserCannotSwitchModes()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.NormalUser, _fixture.NormalPass);

        var reply = await client.Execute("SITE LOG EVERYTHING");

        Assert.False(reply.Success, $"SITE LOG EVERYTHING unexpectedly succeeded: {reply.Code} {reply.Message}");
    }

    [Fact]
    public void QuickLogShutdown_FlushesPendingEntries()
    {
        var logger = QuickLogFactory.CreateTestLogger(QuickLogMode.Everything, out var memory);

        logger.Log(FtpLogLevel.Info, "before shutdown");
        logger.Flush();
        var entries = memory.Snapshot();
        logger.Dispose();

        Assert.Contains(entries, e => e.Message == "before shutdown");
    }

    [Fact]
    public void QuickLogFactory_FileLockFallsBackToProcessSpecificFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"amftpd-quicklog-{Guid.NewGuid():N}");
        var logDir = Path.Combine(root, "logs");
        var lockedLog = Path.Combine(logDir, "amftpd.log");
        var configPath = Path.Combine(root, "amftpd.json");

        Directory.CreateDirectory(logDir);
        File.WriteAllText(configPath, """
        {
          "Logging": {
            "Mode": "everything",
            "TextLogPath": "logs/amftpd.log",
            "BinaryLogPath": "logs/amftpd.qlbin",
            "Console": false,
            "Binary": false
          }
        }
        """);

        using var lockStream = new FileStream(
            lockedLog,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        try
        {
            using var logger = QuickLogFactory.Create(configPath);
            logger.Log(FtpLogLevel.Info, "fallback log write");
            logger.Flush();

            Assert.Equal(QuickLogMode.Everything, logger.Mode);
        }
        finally
        {
            lockStream.Dispose();
            var fallbackLogs = Directory.GetFiles(logDir, "amftpd.*.log");
            try { Directory.Delete(root, true); } catch { }

            Assert.NotEmpty(fallbackLogs);
        }
    }

    private static void LogAllLevels(QuickLogFtpLogger logger)
    {
        logger.Log(FtpLogLevel.Trace, "trace");
        logger.Log(FtpLogLevel.Debug, "debug");
        logger.Log(FtpLogLevel.Info, "info");
        logger.Log(FtpLogLevel.Warn, "warn");
        logger.Log(FtpLogLevel.Error, "error");
        logger.Log(FtpLogLevel.Critical, "critical");
    }
}
