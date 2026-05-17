using System.Text;

namespace amFTPd.Scripting.Tcl;

/// <summary>
/// Orchestrates the execution of TCL scripts in a glFTPd-compatible environment.
/// </summary>
public sealed class GlftpdTclRunner
{
    private readonly string _tclScriptsRoot;

    public GlftpdTclRunner(string tclScriptsRoot)
    {
        _tclScriptsRoot = tclScriptsRoot;
    }

    public async Task<TclResult> ExecuteSiteCommandAsync(
        string scriptPath,
        object user,
        string[] args,
        object session)
    {
        using var interp = new TclInterpreter();

        // 1. Populate glFTPd standard environment
        PopulateEnvironment(interp, user, session, args);

        // 2. Resolve absolute path
        var fullPath = Path.IsPathRooted(scriptPath)
            ? scriptPath
            : Path.GetFullPath(Path.Combine(_tclScriptsRoot, scriptPath));

        // 3. Run it
        var result = interp.EvaluateFile(fullPath);

        return result;
    }

    private void PopulateEnvironment(
        TclInterpreter interp,
        object user,
        object session,
        string[] args)
    {
        // Use reflection to get properties without a project reference
        var userType = user.GetType();
        var sessionType = session.GetType();

        var userName = userType.GetProperty("UserName")?.GetValue(user)?.ToString() ?? "Unknown";
        var primaryGroup = userType.GetProperty("PrimaryGroup")?.GetValue(user)?.ToString()
                        ?? userType.GetProperty("GroupName")?.GetValue(user)?.ToString()
                        ?? "NoGroup";
        var credits = (long)(userType.GetProperty("Credits")?.GetValue(user) ?? 0L);
        var isAdmin = (bool)(userType.GetProperty("IsAdmin")?.GetValue(user) ?? false);
        var isSiteop = (bool)(userType.GetProperty("IsSiteop")?.GetValue(user) ?? false);

        var cwd = sessionType.GetProperty("Cwd")?.GetValue(session)?.ToString() ?? "/";
        var remoteEndPoint = sessionType.GetProperty("RemoteEndPoint")?.GetValue(session);
        var ip = "0.0.0.0";
        var port = "0";

        if (remoteEndPoint != null)
        {
            var epType = remoteEndPoint.GetType();
            ip = epType.GetProperty("Address")?.GetValue(remoteEndPoint)?.ToString() ?? "0.0.0.0";
            port = epType.GetProperty("Port")?.GetValue(remoteEndPoint)?.ToString() ?? "0";
        }

        interp.SetVariable("USER", userName);
        interp.SetVariable("GROUP", primaryGroup);
        interp.SetVariable("FLAGS", isAdmin ? "3" : (isSiteop ? "1" : "0"));
        interp.SetVariable("CREDITS", (credits / 1024).ToString());
        interp.SetVariable("IP", ip);
        interp.SetVariable("PORT", port);
        interp.SetVariable("PWD", cwd);

        // Populate $args list in TCL
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append('\"').Append(arg.Replace("\"", "\\\"")).Append('\"');
        }
        interp.SetVariable("args", sb.ToString());
    }
}
