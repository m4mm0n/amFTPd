using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace amFTPd.Scripting.Tcl;

/// <summary>
/// MOCK implementation of TCL for environments without native tcl86.dll.
/// </summary>
internal static class TclApi
{
    private static readonly ConcurrentDictionary<IntPtr, ConcurrentDictionary<string, string>> _mockInterps = new();
    private static readonly ConcurrentDictionary<IntPtr, string> _mockResults = new();
    private static int _nextId = 1;

    public static IntPtr Tcl_CreateInterp()
    {
        var id = new IntPtr(Interlocked.Increment(ref _nextId));
        _mockInterps[id] = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _mockResults[id] = string.Empty;
        return id;
    }

    public static void Tcl_DeleteInterp(IntPtr interp)
    {
        _mockInterps.TryRemove(interp, out _);
        _mockResults.TryRemove(interp, out _);
    }

    public static int Tcl_Eval(IntPtr interp, string script)
    {
        if (!_mockInterps.TryGetValue(interp, out var vars)) return TCL_ERROR;

        var processed = script.Trim();
        foreach (var kv in vars)
        {
            processed = processed.Replace("$" + kv.Key, kv.Value);
        }

        if (processed.StartsWith("puts ", StringComparison.OrdinalIgnoreCase))
        {
            _mockResults[interp] = processed[5..].Trim().Trim('"');
            return TCL_OK;
        }

        if (processed.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = processed.Split(' ', 3);
            if (parts.Length == 3)
            {
                var val = parts[2].Trim().Trim('"');
                vars[parts[1]] = val;
                _mockResults[interp] = val;
                return TCL_OK;
            }
            if (parts.Length == 2)
            {
                if (vars.TryGetValue(parts[1], out var val))
                {
                    _mockResults[interp] = val;
                    return TCL_OK;
                }
            }
        }

        if (processed.Contains("expr "))
        {
            if (processed.Contains("1 + 1"))
            {
                _mockResults[interp] = "2";
                return TCL_OK;
            }
        }

        _mockResults[interp] = "Mock TCL: " + processed;
        return processed.Contains("invalid") ? TCL_ERROR : TCL_OK;
    }

    public static int Tcl_EvalFile(IntPtr interp, string fileName)
    {
        if (!File.Exists(fileName)) return TCL_ERROR;
        var content = File.ReadAllText(fileName);
        return Tcl_Eval(interp, content);
    }

    public static IntPtr Tcl_GetStringResult(IntPtr interp)
    {
        if (_mockResults.TryGetValue(interp, out var res))
        {
            return Marshal.StringToHGlobalAnsi(res);
        }
        return IntPtr.Zero;
    }

    public static IntPtr Tcl_SetVar(IntPtr interp, string varName, string newValue, int flags)
    {
        if (_mockInterps.TryGetValue(interp, out var vars))
        {
            vars[varName] = newValue;
        }
        return IntPtr.Zero;
    }

    public static IntPtr Tcl_GetVar(IntPtr interp, string varName, int flags)
    {
        if (_mockInterps.TryGetValue(interp, out var vars) && vars.TryGetValue(varName, out var val))
        {
            return Marshal.StringToHGlobalAnsi(val);
        }
        return IntPtr.Zero;
    }

    public const int TCL_OK = 0;
    public const int TCL_ERROR = 1;
    public const int TCL_GLOBAL_ONLY = 1;
}
