using System;
using System.IO;
using System.Collections.Generic;
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
