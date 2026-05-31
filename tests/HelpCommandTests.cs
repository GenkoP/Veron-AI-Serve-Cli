using System;
using System.IO;
using Veron;
using Xunit;

namespace Veron.Tests;

public class HelpCommandTests
{
    [Fact]
    public void CmdHelp_Has_Entries_For_All_Commands()
    {
        var commands = new[] { "cat", "ls", "list", "create", "serve", "claude", "copilot", "run", "ps", "stop", "remove", "rm", "help", "version" };

        foreach (var cmd in commands)
        {
            Assert.NotNull(CmdHelp.Get(cmd));
        }
    }

    [Fact]
    public void CmdHelp_Serve_Includes_Options()
    {
        var entry = CmdHelp.Get("serve");
        Assert.NotNull(entry);

        var options = entry!.Value.Options;
        Assert.Contains(options, o => o.StartsWith("--port"));
        Assert.Contains(options, o => o.StartsWith("--context"));
        Assert.Contains(options, o => o.StartsWith("--alias"));
    }

    [Fact]
    public void CmdHelp_Ps_Has_No_Options()
    {
        var entry = CmdHelp.Get("ps");
        Assert.NotNull(entry);
        Assert.Empty(entry!.Value.Options);
    }

    [Fact]
    public void CmdHelp_UnknownCommand_Returns_Null()
    {
        Assert.Null(CmdHelp.Get("foobar"));
    }

    [Fact]
    public void CmdHelp_Run_Prints_Command_Help_To_Stdout()
    {
        using var sw = new StringWriter();
        Console.SetOut(sw);

        CmdHelp.Run("ps");

        string output = sw.ToString();
        Assert.Contains("USAGE", output);
        Assert.Contains("veron ps", output);
    }

    [Fact]
    public void CmdHelp_Run_Null_Prints_TopLevel()
    {
        using var sw = new StringWriter();
        Console.SetOut(sw);

        CmdHelp.Run(null);

        string output = sw.ToString();
        Assert.Contains("COMMANDS", output);
        Assert.Contains("--models-dir", output);
        Assert.Contains("veron <command> --help", output);
    }
}
