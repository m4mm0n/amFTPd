namespace amFTPd.Core;

internal static class FtpPath
{
    // Normalize to posix-style within virtual root.
    public static string Normalize(string current, string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return current;
        var p = input.Replace('\\', '/');
        if (p.StartsWith('/')) return Collapse(p);
        return Collapse($"{current.TrimEnd('/')}/{p}");
    }

    private static string Collapse(string p)
    {
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new Stack<string>();
        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.Pop();
                continue;
            }
            stack.Push(part);
        }
        var arr = stack.Reverse().ToArray();
        return "/" + string.Join('/', arr);
    }
}