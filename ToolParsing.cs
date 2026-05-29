namespace Veron;

/// <summary>
/// Per-tool CLI parameter configuration parsed from TOOL blocks in a modelfile.
/// </summary>
public class ToolConfig
{
    public string Name                           { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>
/// Static methods for parsing TOOL blocks from modelfiles.
/// Exposed as a test helper so the test project can call them.
/// </summary>
public static class ProgramTestHelper
{
    public static Dictionary<string, ToolConfig> ParseToolBlocks(string path)
    {
        var result = new Dictionary<string, ToolConfig>();
        var lines   = File.ReadAllLines(path);

        string? currentToolName = null;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            // ── Start of a TOOL block ────────────────────────
            if (line.StartsWith("TOOL", StringComparison.OrdinalIgnoreCase))
            {
                if (currentToolName is not null)
                    throw new InvalidOperationException("Nested TOOL blocks are not allowed: '" + currentToolName + "' is already open");

                string toolName = line[4..].Trim();
                if (toolName.Length == 0)
                    throw new InvalidOperationException("'TOOL' directive requires a tool name");

                currentToolName = toolName;
                result[currentToolName] = new ToolConfig { Name = currentToolName };
                continue;
            }

            // ── End of a TOOL block ──────────────────────────
            if (line.StartsWith("END_TOOL", StringComparison.OrdinalIgnoreCase))
            {
                if (currentToolName is null)
                    throw new InvalidOperationException("'END_TOOL' without matching 'TOOL' directive");

                currentToolName = null;
                continue;
            }

            // ── PARAMETER inside a TOOL block ────────────────
            if (currentToolName is not null && line.StartsWith("PARAMETER", StringComparison.OrdinalIgnoreCase))
            {
                string rest = line[9..].Trim(); // everything after "PARAMETER"
                int spaceIdx = rest.IndexOf(' ');
                if (spaceIdx < 0) continue;

                string key   = rest[..spaceIdx].Trim();
                string value = rest[(spaceIdx + 1)..].Trim();

                // Strip quotes from value
                if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') ||
                                          (value[0] == '\'' && value[^1] == '\'')))
                    value = value[1..^1];

                result[currentToolName].Parameters[key] = value;
                continue;
            }
        }

        // If we hit EOF with an open TOOL block, that's an error
        if (currentToolName is not null)
            throw new InvalidOperationException($"Unclosed TOOL block for '{currentToolName}' — missing END_TOOL");

        return result;
    }
}
