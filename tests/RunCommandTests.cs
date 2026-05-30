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
        Assert.True(cmd.Contains("-m"));
        Assert.True(cmd.Contains("--alias"));
        Assert.True(cmd.Contains("-ngl"));
        Assert.True(cmd.Contains("--flash-attn"));
        Assert.True(cmd.Contains("--jinja"));
        Assert.True(cmd.Contains("--color"));
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

        Assert.True(cmd.Contains("--prompt"));
        Assert.True(cmd.Contains("--single-turn"));
        Assert.True(cmd.Contains("Hello world"));
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

        Assert.False(cmd.Contains("--single-turn"));
        Assert.False(cmd.Contains("--prompt"));
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

        Assert.False(cmd.Contains("--color"));
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

        Assert.True(cmd.Contains("--temperature"));
        Assert.True(cmd.Contains("0.30"));
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

        Assert.True(cmd.Contains("--top-p"));
        Assert.True(cmd.Contains("0.50"));
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

        Assert.False(cmd.Contains("--flash-attn"));
    }
}
