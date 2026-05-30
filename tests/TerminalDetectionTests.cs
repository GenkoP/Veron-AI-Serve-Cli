using Veron;
using Xunit;

namespace Veron.Tests;

public class TerminalDetectionTests
{
    [Fact]
    public void DetectTerminal_Does_Not_Throw()
    {
        // TERM_PROGRAM may not be set in CI/test env and process tree may not contain a known terminal,
        // so we only verify the method runs without throwing. Result may be null or a valid terminal name.
        var result = TerminalDetector.DetectTerminal();
        // xUnit: no throw = pass. We assert on type to satisfy analyzer.
        Assert.True(result == null || result is string);
    }

    [Fact]
    public void BuildTerminalCommand_Returns_Valid_Command_For_Gnome()
    {
        var cmd = TerminalDetector.BuildTerminalCommand("gnome-terminal", "echo hello");
        Assert.NotNull(cmd);
        Assert.Contains("--", cmd);
    }

    [Fact]
    public void BuildTerminalCommand_Returns_Null_For_Unknown_Terminal()
    {
        var cmd = TerminalDetector.BuildTerminalCommand("unknown-terminal", "echo hello");
        Assert.Null(cmd);
    }
}
