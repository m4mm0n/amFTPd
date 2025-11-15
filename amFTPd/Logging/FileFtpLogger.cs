using System.Collections.Concurrent;
using System.Text;

namespace amFTPd.Logging;

/// <summary>
/// Provides a logger implementation that writes FTP log messages to a file.
/// </summary>
/// <remarks>This logger writes log messages to a specified file, appending new entries to the file.  It supports
/// asynchronous logging using a background worker thread to ensure minimal  impact on application performance. The
/// logger is thread-safe and can be used in  multi-threaded applications.  The log messages are formatted using a
/// customizable formatter, or a default format  if no formatter is provided. Exceptions associated with log messages
/// are also written  to the file, if present.  The logger must be disposed when no longer needed to ensure proper
/// cleanup of resources  such as the file stream and background worker thread.</remarks>
public sealed class FileFtpLogger : IFtpLogger, IDisposable
{
    private readonly FileFtpLoggerOptions _options;
    private readonly BlockingCollection<LogItem> _queue;
    private readonly CancellationTokenSource _cts;
    private readonly Task _worker;
    private readonly StreamWriter _writer;
    private bool _disposed;

    private readonly struct LogItem
    {
        public LogItem(FtpLogLevel level, string message, Exception? exception, DateTime timestamp)
        {
            Level = level;
            Message = message;
            Exception = exception;
            Timestamp = timestamp;
        }

        public FtpLogLevel Level { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public DateTime Timestamp { get; }
    }

    public FileFtpLogger(FileFtpLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.FilePath))
            throw new ArgumentException("FilePath must be set.", nameof(options));

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_options.FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Open file for append, allow readers
        var fileStream = new FileStream(
            _options.FilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);

        _writer = new StreamWriter(fileStream, Encoding.UTF8)
        {
            AutoFlush = true
        };

        _queue = new BlockingCollection<LogItem>(new ConcurrentQueue<LogItem>());
        _cts = new CancellationTokenSource();
        _worker = Task.Factory.StartNew(
            WorkerLoop,
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public void Log(FtpLogLevel level, string message, Exception? ex = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileFtpLogger));

        if (level < _options.MinLevel)
            return;

        if (message == null) message = string.Empty;

        var item = new LogItem(level, message, ex, DateTime.UtcNow);

        // If the queue is already marked complete, just ignore.
        if (!_queue.IsAddingCompleted)
        {
            try
            {
                _queue.Add(item);
            }
            catch (InvalidOperationException)
            {
                // Adding completed - ignore
            }
        }
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable(_cts.Token))
            {
                var line = Format(item);
                _writer.WriteLine(line);

                if (item.Exception is not null)
                {
                    _writer.WriteLine(item.Exception.ToString());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            try
            {
                _writer.WriteLine($"[FileFtpLogger] WorkerLoop encountered error: {ex}");
            }
            catch
            {
                // last resort: swallow
            }
        }
        finally
        {
            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private string Format(LogItem item)
    {
        if (_options.Formatter is not null)
        {
            return _options.Formatter(item.Level, item.Message, item.Exception, item.Timestamp);
        }

        // Default format: [2025-11-15T12:34:56.789Z] [Information] message
        return $"[{item.Timestamp:O}] [{item.Level}] {item.Message}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _queue.CompleteAdding();

        try
        {
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // ignore
        }

        _cts.Dispose();
        _queue.Dispose();
        // _writer is disposed in WorkerLoop finally block
    }
}