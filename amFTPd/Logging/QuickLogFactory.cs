using System.Text.Json;
using QuickLog;
using QuickLog.Core;
using QuickLog.Loggers;

namespace amFTPd.Logging;

/// <summary>
/// Creates QuickLog-backed daemon loggers from amFTPd config and CLI overrides.
/// </summary>
public static class QuickLogFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static QuickLogFtpLogger Create(
        string configFilePath,
        QuickLogMode? overrideMode = null)
    {
        var options = LoadOptions(configFilePath);
        if (overrideMode is { } mode)
            options = options.WithMode(mode);

        var resolvedMode = options.GetMode();
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configFilePath))
                        ?? Environment.CurrentDirectory;
        var textPath = ResolvePath(configDir, options.TextLogPath);
        var binaryPath = ResolvePath(configDir, options.BinaryLogPath);

        Directory.CreateDirectory(Path.GetDirectoryName(textPath) ?? configDir);
        Directory.CreateDirectory(Path.GetDirectoryName(binaryPath) ?? configDir);

        try
        {
            var quickLog = CreateQuickLog(options, textPath, binaryPath);
            return new QuickLogFtpLogger(quickLog, resolvedMode, LogManager.Shutdown);
        }
        catch (IOException ex)
        {
            LogManager.Shutdown();

            var fallbackTextPath = CreateFallbackPath(textPath);
            var fallbackBinaryPath = CreateFallbackPath(binaryPath);
            var quickLog = CreateQuickLog(options, fallbackTextPath, fallbackBinaryPath);
            var logger = new QuickLogFtpLogger(quickLog, resolvedMode, LogManager.Shutdown);
            logger.Log(
                FtpLogLevel.Warn,
                $"Configured QuickLog file '{textPath}' is unavailable; using '{fallbackTextPath}'.",
                ex);
            return logger;
        }
    }

    public static QuickLogFtpLogger CreateTestLogger(out MemoryQuickLogger memoryLogger) =>
        CreateTestLogger(QuickLogMode.Something, out memoryLogger);

    public static QuickLogFtpLogger CreateCommandLogger(QuickLogMode? overrideMode = null)
    {
        var mode = overrideMode ?? QuickLogMode.Something;
        var memoryLogger = new MemoryQuickLogger(1024);
        return new QuickLogFtpLogger(memoryLogger, mode);
    }

    public static QuickLogFtpLogger CreateTestLogger(
        QuickLogMode mode,
        out MemoryQuickLogger memoryLogger)
    {
        memoryLogger = new MemoryQuickLogger(4096);
        return new QuickLogFtpLogger(memoryLogger, mode);
    }

    private static QuickLogOptions LoadOptions(string configFilePath)
    {
        if (!File.Exists(configFilePath))
            return QuickLogOptions.Default;

        try
        {
            using var stream = File.OpenRead(configFilePath);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (!doc.RootElement.TryGetProperty("Logging", out var logging) &&
                !doc.RootElement.TryGetProperty("logging", out logging))
            {
                return QuickLogOptions.Default;
            }

            return logging.Deserialize<QuickLogOptions>(JsonOptions)
                   ?? QuickLogOptions.Default;
        }
        catch
        {
            return QuickLogOptions.Default;
        }
    }

    private static string ResolvePath(string baseDir, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDir, path));
    }

    private static IQuickLog CreateQuickLog(
        QuickLogOptions options,
        string textPath,
        string binaryPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(textPath) ?? Environment.CurrentDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(binaryPath) ?? Environment.CurrentDirectory);

        var loggerOptions = new LoggerOptions()
            .WithFile(textPath)
            .WithConsole(options.Console)
            .WithAsyncOnly(AsyncDropPolicy.DropOldest, LogType.Trace)
            .WithAsyncQueueCapacity(Math.Max(options.QueueCapacity, 128))
            .WithRotation(10 * 1024 * 1024, 10, false);

        if (options.Binary)
            loggerOptions = loggerOptions.WithBinaryLog(binaryPath);

        LogManager.ConfigureDefault(loggerOptions);
        return LogManager.GetDefaultLogger();
    }

    private static string CreateFallbackPath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var suffix = $"{Environment.ProcessId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        return Path.Combine(directory, $"{fileName}.{suffix}{extension}");
    }
}
