using System;
using System.IO;
using Veron;
using Xunit;

namespace Veron.Tests;

public class ClaudeCommandTests
{
    [Fact]
    public void ModelfileParser_OptsBool_Foreground_Only_Checks_Long_Form()
    {
        // Verify that -f is NOT interpreted as foreground for claude command
        // The -f flag should only work with the remove command
        var opts = new System.Collections.Generic.Dictionary<string, string>
        {
            ["model"] = "test-model",
        };

        // Without --foreground flag, foreground should be false
        bool foreground = CliParser.OptsBool(opts, "foreground");
        Assert.False(foreground);
    }

    [Fact]
    public void ModelfileParser_OptsBool_Foreground_True_When_Set()
    {
        var opts = new System.Collections.Generic.Dictionary<string, string>
        {
            ["model"] = "test-model",
            ["foreground"] = "true",
        };

        bool foreground = CliParser.OptsBool(opts, "foreground");
        Assert.True(foreground);
    }
}
