using System.Collections.Generic;

namespace Veron;

public static class ProgramTestHelper
{
    public static Dictionary<string, ToolConfig> ParseToolBlocks(string path) =>
        ModelfileParser.ParseToolBlocks(path);

    public static List<string> ValidateClaudeCodeParameter(string key, string value) =>
        ClaudeCodeValidator.ValidateClaudeCodeParameter(key, value);

    public static List<string> ValidateToolBlocks(string sourcePath, string name, string modelsDir)
    {
        // This wraps the validation that was previously in Program.ValidateToolBlocks.
        // The name and modelsDir parameters are kept for backward compatibility with tests,
        // but ValidateToolBlocks only needs sourcePath since it validates TOOL blocks only.
        return ClaudeCodeValidator.ValidateToolBlocks(sourcePath);
    }
}
