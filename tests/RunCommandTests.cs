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

    [Fact]
    public void BuildLlamaCmd_Defaults_Include_Required_Flags()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        Assert.Equal("/home/genkop/Workspace/llama-cpp/llama.cpp/build/bin/llama-cli", cmd[0]);
        Assert.Contains("-m", cmd);
        Assert.Contains("--alias", cmd);
        Assert.Contains("-ngl", cmd);
        Assert.Contains("--flash-attn", cmd);
        Assert.Contains("--jinja", cmd);
        Assert.Contains("--color", cmd);
        Assert.Contains("--temperature", cmd);
        Assert.Contains("--top-p", cmd);
    }

    [Fact]
    public void BuildLlamaCmd_Temperature_Defaults_To_08()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        int tempIdx = cmd.IndexOf("--temperature");
        Assert.True(tempIdx >= 0, "--temperature should be present");
        Assert.Equal("0.80", cmd[tempIdx + 1]);
    }

    [Fact]
    public void BuildLlamaCmd_TopP_Defaults_To_09()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        int topPIdx = cmd.IndexOf("--top-p");
        Assert.True(topPIdx >= 0, "--top-p should be present");
        Assert.Equal("0.90", cmd[topPIdx + 1]);
    }

    [Fact]
    public void BuildLlamaCmd_Prompt_Includes_SingleTurn()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
            Prompt    = "Hello world",
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        Assert.Contains("--prompt", cmd);
        Assert.Contains("--single-turn", cmd);
        Assert.Contains("Hello world", cmd);
    }

    [Fact]
    public void BuildLlamaCmd_NoPrompt_Excludes_SingleTurn()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        Assert.DoesNotContain("--single-turn", cmd);
        Assert.DoesNotContain("--prompt", cmd);
    }

    [Fact]
    public void BuildLlamaCmd_NoColor_Excludes_Color_Flag()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
            Color     = false,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        Assert.DoesNotContain("--color", cmd);
    }

    [Fact]
    public void BuildLlamaCmd_Temperature_Includes_Temp_Flag()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
            Temperature = 0.3f,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        Assert.Contains("--temperature", cmd);
        Assert.Contains("0.30", cmd);
    }

    [Fact]
    public void BuildLlamaCmd_TopP_Includes_TopP_Flag()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
            TopP      = 0.5f,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        Assert.Contains("--top-p", cmd);
        Assert.Contains("0.50", cmd);
    }

    [Fact]
    public void BuildLlamaCmd_NGpuLayers_Defaults_To_Minus_1()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        int nglIdx = cmd.IndexOf("-ngl");
        Assert.True(nglIdx >= 0, "-ngl should be present");
        Assert.Equal("-1", cmd[nglIdx + 1]);
    }

    [Fact]
    public void BuildLlamaCmd_NoFlashAttention_Excludes_FlashAttn()
    {
        var cfg = new ModelConfig
        {
            ModelPath = "/home/genkop/Workspace/llama-cpp/models/test.gguf",
            Alias     = "test-model",
            Context   = 2048,
            RepeatPenalty = 1.1f,
            Fa        = false,
        };

        var cmd = ProgramTestHelper.BuildLlamaCmd(cfg);

        Assert.DoesNotContain("--flash-attn", cmd);
    }
}
