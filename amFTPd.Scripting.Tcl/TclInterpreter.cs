using System.Runtime.InteropServices;

namespace amFTPd.Scripting.Tcl;

/// <summary>
/// Managed wrapper for a native TCL interpreter instance.
/// </summary>
public sealed class TclInterpreter : IDisposable
{
    private IntPtr _interp;
    private bool _disposed;

    public TclInterpreter()
    {
        _interp = TclApi.Tcl_CreateInterp();
        if (_interp == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create native TCL interpreter.");
    }

    public void SetVariable(string name, string value)
    {
        ThrowIfDisposed();
        TclApi.Tcl_SetVar(_interp, name, value, TclApi.TCL_GLOBAL_ONLY);
    }

    public string GetVariable(string name)
    {
        ThrowIfDisposed();
        var ptr = TclApi.Tcl_GetVar(_interp, name, TclApi.TCL_GLOBAL_ONLY);
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
    }

    public TclResult Evaluate(string script)
    {
        ThrowIfDisposed();
        var code = TclApi.Tcl_Eval(_interp, script);
        return new TclResult(code, GetStringResult());
    }

    public TclResult EvaluateFile(string fileName)
    {
        ThrowIfDisposed();
        if (!File.Exists(fileName))
            return new TclResult(TclApi.TCL_ERROR, $"File not found: {fileName}");

        var code = TclApi.Tcl_EvalFile(_interp, fileName);
        return new TclResult(code, GetStringResult());
    }

    private string GetStringResult()
    {
        var ptr = TclApi.Tcl_GetStringResult(_interp);
        if (ptr == IntPtr.Zero) return string.Empty;
        try
        {
            return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TclInterpreter));
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_interp != IntPtr.Zero)
        {
            TclApi.Tcl_DeleteInterp(_interp);
            _interp = IntPtr.Zero;
        }
        _disposed = true;
    }
}

public record TclResult(int Code, string Result)
{
    public bool Success => Code == TclApi.TCL_OK;
}
