using System;
using System.IO;
using System.Collections.Generic;
using Veron;
using Xunit;

namespace Veron.Tests;

public class ToolParsingTests
{
    [Fact]
    public void ParseToolBlocks_Returns_Empty_When_No_Tools()
    {
        var content = @"FROM model.gguf";
        using var tmp = CreateTempFile(content);
        var result = ProgramTestHelper.ParseToolBlocks(tmp.Path);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseToolBlocks_Returns_Claude_Code_Config()
    {
        var content = @"FROM model.gguf
TOOL claude-code
  PARAMETER permission-mode auto
  PARAMETER tools Bash,Edit,Read
END_TOOL";
        using var tmp = CreateTempFile(content);
        var result = ProgramTestHelper.ParseToolBlocks(tmp.Path);
        Assert.True(result.ContainsKey("claude-code"));
        var cc = result["claude-code"];
        Assert.Equal("auto", cc.Parameters["permission-mode"]);
        Assert.Equal("Bash,Edit,Read", cc.Parameters["tools"]);
    }

    [Fact]
    public void ParseToolBlocks_Returns_Multiple_Tools()
    {
        var content = @"FROM model.gguf
TOOL claude-code
  PARAMETER effort high
END_TOOL
TOOL cursor
  PARAMETER max-turns 50
END_TOOL";
        using var tmp = CreateTempFile(content);
        var result = ProgramTestHelper.ParseToolBlocks(tmp.Path);
        Assert.Equal(2, result.Count);
        Assert.Equal("high", result["claude-code"].Parameters["effort"]);
        Assert.Equal("50", result["cursor"].Parameters["max-turns"]);
    }

    [Fact]
    public void ParseToolBlocks_Handles_Quoted_Values()
    {
        var content = @"FROM model.gguf
TOOL claude-code
  PARAMETER append-system-prompt ""You are helpful""
END_TOOL";
        using var tmp = CreateTempFile(content);
        var result = ProgramTestHelper.ParseToolBlocks(tmp.Path);
        Assert.Equal("You are helpful", result["claude-code"].Parameters["append-system-prompt"]);
    }

    [Fact]
    public void ParseToolBlocks_Throws_On_Nested_Tools()
    {
        var content = @"FROM model.gguf
TOOL claude-code
  TOOL nested
END_TOOL";
        using var tmp = CreateTempFile(content);
        Assert.Throws<InvalidOperationException>(() => ProgramTestHelper.ParseToolBlocks(tmp.Path));
    }

    [Fact]
    public void ParseToolBlocks_Throws_On_END_TOOL_Without_TOOL()
    {
        var content = @"FROM model.gguf
END_TOOL";
        using var tmp = CreateTempFile(content);
        Assert.Throws<InvalidOperationException>(() => ProgramTestHelper.ParseToolBlocks(tmp.Path));
    }

    [Fact]
    public void ParseToolBlocks_Throws_On_Unclosed_TOOL()
    {
        var content = @"FROM model.gguf
TOOL claude-code
  PARAMETER effort high";
        using var tmp = CreateTempFile(content);
        Assert.Throws<InvalidOperationException>(() => ProgramTestHelper.ParseToolBlocks(tmp.Path));
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Valid_PermissionMode()
    {
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("permission-mode", "auto");
        Assert.Empty(errors);
        var errors2 = ProgramTestHelper.ValidateClaudeCodeParameter("permission-mode", "plan");
        Assert.Empty(errors2);
        var errors3 = ProgramTestHelper.ValidateClaudeCodeParameter("permission-mode", "bypassPermissions");
        Assert.Empty(errors3);
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Invalid_PermissionMode()
    {
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("permission-mode", "invalid-mode");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Valid_Effort()
    {
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("effort", "high");
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Invalid_Effort()
    {
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("effort", "ultra");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Valid_MaxTurns()
    {
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("max-turns", "10");
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Invalid_MaxTurns()
    {
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("max-turns", "abc");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Valid_MaxBudgetUsd()
    {
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("max-budget-usd", "5.00");
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Invalid_MaxBudgetUsd()
    {
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("max-budget-usd", "not-a-number");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateClaudeCodeParameter_Unknown_Passthrough()
    {
        // Unknown params pass through without validation
        var errors = ProgramTestHelper.ValidateClaudeCodeParameter("some-future-flag", "anything");
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateModelfile_Rejects_Invalid_Tool_Param()
    {
        var content = @"FROM MiniCPM5-1B-Q4_K_M.gguf
TOOL claude-code
  PARAMETER effort invalid-value
END_TOOL";
        using var tmp = CreateTempFile(content);
        var errors = ProgramTestHelper.ValidateToolBlocks(tmp.Path, "test-name", "/home/genkop/Workspace/llama-cpp/models");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateModelfile_Accepts_Valid_Tool_Param()
    {
        var content = @"FROM MiniCPM5-1B-Q4_K_M.gguf
TOOL claude-code
  PARAMETER permission-mode auto
  PARAMETER effort high
END_TOOL";
        using var tmp = CreateTempFile(content);
        var errors = ProgramTestHelper.ValidateToolBlocks(tmp.Path, "test-name", "/home/genkop/Workspace/llama-cpp/models");
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateModelfile_Passthrough_Unknown_Tool_Param()
    {
        var content = @"FROM MiniCPM5-1B-Q4_K_M.gguf
TOOL claude-code
  PARAMETER some-future-flag anything
END_TOOL";
        using var tmp = CreateTempFile(content);
        var errors = ProgramTestHelper.ValidateToolBlocks(tmp.Path, "test-name", "/home/genkop/Workspace/llama-cpp/models");
        Assert.Empty(errors);
    }

    [Fact]
    public void ParseToolBlocks_Bare_Flag_Stores_Null()
    {
        var content = @"FROM model.gguf
TOOL copilot
  PARAMETER yolo
  PARAMETER effort high
END_TOOL";
        using var tmp = CreateTempFile(content);
        var result = ProgramTestHelper.ParseToolBlocks(tmp.Path);
        Assert.True(result.ContainsKey("copilot"));
        Assert.Null(result["copilot"].Parameters["yolo"]); // bare flag → null
        Assert.Equal("high", result["copilot"].Parameters["effort"]); // normal param unchanged
    }

    [Fact]
    public void ValidateModelfile_Rejects_Nested_Tools()
    {
        var content = @"FROM MiniCPM5-1B-Q4_K_M.gguf
TOOL claude-code
  TOOL nested
END_TOOL";
        using var tmp = CreateTempFile(content);
        var errors = ProgramTestHelper.ValidateToolBlocks(tmp.Path, "test-name", "/home/genkop/Workspace/llama-cpp/models");
        Assert.NotEmpty(errors);
    }

    static TempFile CreateTempFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return new TempFile(path);
    }

    class TempFile : IDisposable
    {
        public string Path { get; }
        public TempFile(string path) => Path = path;
        public void Dispose() => File.Delete(Path);
    }
}
