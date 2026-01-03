using System.Text;
using System.Threading.Channels;

namespace amFTPd.Core.Irc;

/// <summary>
/// Provides asynchronous logging of IRC wire traffic to a file, recording message direction and timestamps for
/// diagnostic or auditing purposes.
/// </summary>
/// <remarks>IrcWireLogger writes log entries to the specified file in a background task, ensuring that logging
/// does not block the calling thread. Log entries include a timestamp and a direction indicator (such as sent,
/// received, informational, or error). This class is intended for internal use and is not thread-safe for concurrent
/// disposal. Call DisposeAsync to ensure all log entries are flushed and resources are released.</remarks>
internal sealed class IrcWireLogger : IDisposable
{
    private readonly StreamWriter _writer;

    public IrcWireLogger(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
    }

    public void Send(string line)
        => Log(">>", line);

    public void Receive(string line)
        => Log("<<", line);

    public void Info(string msg)
        => _writer.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] !! {msg}");

    internal void Log(string dir, string line)
    {
#if DEBUG
        _writer.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {dir} {line}");
#else
        _writer.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {dir} {Anonymize(line)}");
#endif
    }

    private static string Anonymize(string line)
    {
        if (line.StartsWith("PING")) return "PING";
        if (line.StartsWith("PONG")) return "PONG";
        if (line.StartsWith("NICK")) return "NICK <redacted>";
        if (line.StartsWith("USER")) return "USER <redacted>";
        if (line.StartsWith("JOIN")) return "JOIN <redacted>";
        if (line.StartsWith("PRIVMSG")) return "PRIVMSG <redacted>";
        return "<redacted>";
    }

    public void Dispose() => _writer.Dispose();
}