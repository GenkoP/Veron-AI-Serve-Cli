using System;
using System.IO;
using Veron;
using Xunit;

namespace Veron.Tests;

public class RemoveCommandTests
{
    [Fact]
    public void Short_Force_Flag_Is_Parsed()
    {
        var opts = CliParser.ParseOpts(new[] { "qwopus", "-f" });
        Assert.Equal("qwopus", opts["model"]);
        Assert.True(CliParser.OptsBool(opts, "force"));
    }

    [Fact]
    public void Remove_Confirmation_Prompt_Flow()
    {
        string modelName = "test-model";
        string expectedPrompt = "Remove profile " + modelName + "? [y/N]";
        Assert.Equal(expectedPrompt, CmdRemove.ConfirmationPrompt(modelName));
    }
}
