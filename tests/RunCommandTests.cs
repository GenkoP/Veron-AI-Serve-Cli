using System;
using Veron;
using Xunit;

namespace Veron.Tests;

public class RunCommandTests
{
    [Fact]
    public void ModelConfig_Has_Run_Command_Fields()
    {
        var cfg = new ModelConfig();

        // New fields should exist with correct defaults
        Assert.True(cfg.Color);
        Assert.False(cfg.Temperature.HasValue);
        Assert.False(cfg.TopP.HasValue);
        Assert.Null(cfg.Prompt);
    }
}
