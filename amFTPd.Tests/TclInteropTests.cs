using amFTPd.Scripting.Tcl;
using Xunit;

namespace amFTPd.Tests;

public class TclInteropTests
{
    [Fact]
    public void Can_Initialize_And_Delete_Interpreter()
    {
        // This test will fail if tcl86.dll is not in the search path.
        // On a real machine, you'd need TCL installed.
        using var interp = new TclInterpreter();
        Assert.NotNull(interp);
    }

    [Fact]
    public void Can_Evaluate_Basic_Tcl()
    {
        using var interp = new TclInterpreter();
        var result = interp.Evaluate("expr 1 + 1");

        Assert.True(result.Success);
        Assert.Equal("2", result.Result);
    }

    [Fact]
    public void Can_Set_And_Get_Variables()
    {
        using var interp = new TclInterpreter();
        interp.SetVariable("user", "testadmin");

        var result = interp.Evaluate("set user");
        Assert.Equal("testadmin", result.Result);

        var val = interp.GetVariable("user");
        Assert.Equal("testadmin", val);
    }

    [Fact]
    public void Can_Handle_Errors()
    {
        using var interp = new TclInterpreter();
        var result = interp.Evaluate("invalid_command");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Result);
    }
}
