namespace amFTPd.Logging;

public sealed class QuickLogAdapter : IFtpLogger
{
    // private readonly IQuickLog _ql;
    // public QuickLogAdapter(IQuickLog ql) => _ql = ql;

    public void Log(FtpLogLevel level, string message, Exception? ex = null)
    {
        // Map to QuickLog levels here.
        // _ql.Log(...);
        Console.WriteLine($"[QL:{level}] {message}");
        if (ex != null) Console.WriteLine(ex);
    }
}