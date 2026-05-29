# Tool Configuration in Modelfiles — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add TOOL blocks to modelfiles so users can configure Claude Code CLI arguments directly in the modelfile, and wire those through to `veron claude <name>`.

**Architecture:** Parse TOOL/END_TOOL sections from modelfile into a `ToolConfig` per block. Validate known Claude Code parameters (permission-mode, effort, etc.) — unknown params pass through. In CmdClaude, look up "claude-code" block and emit its params as `--<key> <value>` flags when launching `claude code`.

**Tech Stack:** .NET 10, C# single-file CLI (Program.cs)

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `Program.cs` | Add ToolConfig struct, TOOL block parsing, validation, and CmdClaude integration |
| Create | `tests/ToolParsingTests.cs` | Tests for TOOL block parsing and parameter validation |
| Create | `tests/Veron.Tests.csproj` | Test project file |

---

### Task 1: Set Up Test Project

**Files:**
- Create: `tests/Veron.Tests.csproj`
- Create: `tests/ToolParsingTests.cs`

- [ ] **Step 1: Create the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the empty test class as a sanity check**

```csharp
using Xunit;

namespace Veron.Tests;

public class ToolParsingTests
{
    [Fact]
    public void SanityCheck()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 3: Run the test to verify the project works**

```bash
cd /home/genkop/Workspace/llama-cpp/Veron-AI-Serve-Cli
dotnet test tests/Veron.Tests.csproj -v minimal
```

Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add tests/
git commit -m "test: add test project for tool configuration"
```

---

### Task 2: Add ToolConfig Data Structure

**Files:**
- Modify: `Program.cs:583-596` (near ModelConfig class)

- [ ] **Step 1: Add the ToolConfig struct right after ModelConfig**

In `Program.cs`, after the `ModelConfig` class (around line 596), add:

```csharp
// ── ToolConfig — per-tool CLI parameter configuration ───────────

class ToolConfig
{
    public string Name           { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}
```

- [ ] **Step 2: Build to verify no compile errors**

```bash
dotnet build --no-incremental
```

Expected: Succeeded

- [ ] **Step 3: Commit**

```bash
git add Program.cs
git commit -m "feat(tool-config): add ToolConfig data structure"
```

---

### Task 3: Add TOOL Block Parsing

**Files:**
- Modify: `Program.cs:299-431` (ParseModelfile area)
- Modify: `tests/ToolParsingTests.cs`

> **Key design:** TOOL blocks are parsed separately from the main modelfile config. We add a new method `ParseToolBlocks` that reads the file and returns a `Dictionary<string, ToolConfig>` keyed by tool name. The existing `ParseModelfile` is left unchanged — it already skips lines it doesn't recognize (TOOL/END_TOOL aren't FROM or PARAMETER).

- [ ] **Step 1: Write the failing test for TOOL block parsing**

In `tests/ToolParsingTests.cs`, replace the sanity check with:

```csharp
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
```

> **Note:** The tests call `ProgramTestHelper.ParseToolBlocks` which doesn't exist yet — these will fail to compile until Task 3 Step 2.

- [ ] **Step 2: Add the ParseToolBlocks method to Program.cs**

Add this method near the other parsing methods (after `ParseModelfile`, around line 431):

```csharp
    static Dictionary<string, ToolConfig> ParseToolBlocks(string path)
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
```

- [ ] **Step 3: Add a test helper to expose ParseToolBlocks for testing**

Since Program.cs methods are static and private, we need a thin helper. Add this at the bottom of `Program.cs` (before the closing brace):

```csharp
    // ── Test Helpers — expose internal methods for testing ──────

    public static class ProgramTestHelper
    {
        public static Dictionary<string, ToolConfig> ParseToolBlocks(string path) =>
            Program.ParseToolBlocks(path);
    }
```

- [ ] **Step 4: Run the tests**

```bash
dotnet test tests/Veron.Tests.csproj -v minimal --no-build
```

Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add Program.cs tests/ToolParsingTests.cs
git commit -m "feat(tool-config): add TOOL block parsing with tests"
```

---

### Task 4: Add Claude Code Parameter Validation

**Files:**
- Modify: `Program.cs` (near the existing `KnownParams` / `ValidateParameterValue` area, around line 452)
- Modify: `tests/ToolParsingTests.cs`

- [ ] **Step 1: Write the failing tests for validation**

Add to `tests/ToolParsingTests.cs`:

```csharp
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
```

- [ ] **Step 2: Add the validation method and known sets to Program.cs**

Add these right after the existing `KnownParams` HashSet (around line 456), but before `IsValidName`:

```csharp
    // ── Claude Code tool parameter validation ────────────────

    static readonly Dictionary<string, HashSet<string>> KnownClaudeCodeModes = new()
    {
        ["permission-mode"] = new() { "auto", "plan", "dontAsk", "bypassPermissions", "default", "acceptEdits" },
        ["effort"] = new() { "low", "medium", "high", "xhigh", "max" }
    };

    static readonly HashSet<string> KnownClaudeCodeIntParams = new()
    {
        "max-turns"
    };

    static readonly HashSet<string> KnownClaudeCodeFloatParams = new()
    {
        "max-budget-usd"
    };

    static List<string> ValidateClaudeCodeParameter(string key, string value)
    {
        var errors = new List<string>();

        // Check if this is a known parameter with specific valid values
        if (KnownClaudeCodeModes.TryGetValue(key, out var validValues))
        {
            if (!validValues.Contains(value))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\". Valid values: {string.Join(", ", validValues)}");
                return errors;
            }
            return errors;
        }

        // Check if this is a known integer parameter
        if (KnownClaudeCodeIntParams.Contains(key))
        {
            if (!int.TryParse(value, out _))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\" — expected an integer");
                return errors;
            }
            return errors;
        }

        // Check if this is a known float parameter
        if (KnownClaudeCodeFloatParams.Contains(key))
        {
            if (!float.TryParse(value, out _))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\" — expected a number");
                return errors;
            }
            return errors;
        }

        // Unknown parameters pass through without validation
        return errors;
    }
```

- [ ] **Step 3: Expose the validation method via ProgramTestHelper**

Update `ProgramTestHelper` in Program.cs:

```csharp
    public static class ProgramTestHelper
    {
        public static Dictionary<string, ToolConfig> ParseToolBlocks(string path) =>
            Program.ParseToolBlocks(path);

        public static List<string> ValidateClaudeCodeParameter(string key, string value) =>
            Program.ValidateClaudeCodeParameter(key, value);
    }
```

- [ ] **Step 4: Run the tests**

```bash
dotnet test tests/Veron.Tests.csproj -v minimal --no-build
```

Expected: All tests PASS (including the parsing tests from Task 3)

- [ ] **Step 5: Commit**

```bash
git add Program.cs tests/ToolParsingTests.cs
git commit -m "feat(tool-config): add Claude Code parameter validation with tests"
```

---

### Task 5: Wire TOOL Blocks into CmdClaude

**Files:**
- Modify: `Program.cs:136-192` (CmdClaude method)

> **Approach:** After loading the config and before launching Claude Code, parse the TOOL blocks from the modelfile. Look up "claude-code" and build CLI args. Add them to the ProcessStartInfo arguments.

- [ ] **Step 1: Modify LoadConfig to also return the modelfile path**

The `CmdClaude` method needs the modelfile path to parse TOOL blocks. `LoadConfig` already computes `mfPath` internally via `FindModelfile`. Change its signature to return it:

Change from:
```csharp
    static ModelConfig LoadConfig(Dictionary<string, string> opts, string modelsDir)
```

To:
```csharp
    static ModelConfig LoadConfig(Dictionary<string, string> opts, string modelsDir, out string? mfPath)
```

The `mfPath` variable is already a local in the method body — it just needs to be surfaced as an `out` parameter. No other change needed inside LoadConfig.

- [ ] **Step 2: Update all callers of LoadConfig**

In `CmdServe` (around line 104):
```csharp
        var cfg = LoadConfig(opts, modelsDir);
```
Change to:
```csharp
        var cfg = LoadConfig(opts, modelsDir, out _);
```

In `CmdClaude` (around line 138):
```csharp
        var cfg = LoadConfig(opts, modelsDir);
```
Change to:
```csharp
        var cfg = LoadConfig(opts, modelsDir, out string? modelfilePath);
```

- [ ] **Step 3: Replace CmdClaude body to wire TOOL blocks**

The full CmdClaude method should now look like this (replace the entire method):

```csharp
    static void CmdClaude(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = LoadConfig(opts, modelsDir, out string? modelfilePath);

        var cmd = BuildLlamaCmd(cfg);

        Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " …");

        var psi = ServerPsi(cmd);
        var serverProc = Process.Start(psi)
                        ?? throw new InvalidOperationException("Failed to start llama-server");

        WritePid(serverProc.Id);
        string baseUrl = "http://localhost:" + cfg.Port;

        if (!WaitForServer(baseUrl, cfg.Wait))
        {
            Console.Error.WriteLine("Error: server did not respond within " + cfg.Wait + "s");
            Environment.Exit(1);
        }

        Console.WriteLine("Server is ready at " + baseUrl);

        // ── Parse TOOL blocks from modelfile ────────────────────────────
        string toolName = "claude-code";
        Dictionary<string, ToolConfig>? toolConfigs = null;

        if (modelfilePath is not null)
        {
            try { toolConfigs = ParseToolBlocks(modelfilePath); }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(1);
            }
        }

        // ── Build claude code CLI arguments ─────────────────────────────
        var claudeArgs = new List<string> { "code" };

        if (toolConfigs is not null && toolConfigs.TryGetValue(toolName, out var toolCfg))
        {
            foreach (var (key, value) in toolCfg.Parameters)
            {
                // Validate known parameters — unknown pass through silently
                var valErrors = ValidateClaudeCodeParameter(key, value);
                if (valErrors.Count > 0)
                {
                    Console.Error.WriteLine(valErrors[0]);
                    Environment.Exit(1);
                }

                claudeArgs.Add("--" + key);
                claudeArgs.Add(value);
            }

            Console.WriteLine("Using TOOL claude-code config from modelfile");
        }

        // ── Launch claude code ─────────────────────────────────────────
        string claudeBin = opts.GetValueOrDefault("claude-bin", DefaultClaudeBin);
        var claudePsi = new ProcessStartInfo(claudeBin, string.Join(" ", claudeArgs.Select(EscapeArg)))
        {
            UseShellExecute = false,
        };
        claudePsi.EnvironmentVariables["ANTHROPIC_BASE_URL"]           = baseUrl;
        claudePsi.EnvironmentVariables["CLAUDE_CODE_ATTRIBUTION_HEADER"] = "0";

        Console.WriteLine();
        Console.WriteLine("Launching claude code …");
        Console.WriteLine("  ANTHROPIC_BASE_URL             = " + baseUrl);
        Console.WriteLine("  CLAUDE_CODE_ATTRIBUTION_HEADER = 0");

        // Print the tool args that were applied
        if (claudeArgs.Count > 1)
            Console.WriteLine("  TOOL claude-code args        = " + string.Join(" ", claudeArgs.Skip(1)));

        Console.WriteLine();

        try
        {
            var claudeProc = Process.Start(claudePsi)
                             ?? throw new FileNotFoundException(claudeBin);
            claudeProc.WaitForExit();
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is Win32Exception)
        {
            Console.Error.WriteLine("Error: couldn't launch '" + claudeBin + "' — make sure it's in $PATH.");
            Environment.Exit(1);
        }

        // ── Tear down the server ─────────────────────────────────────────
        Console.WriteLine("\nclaude code exited. Stopping llama-server …");
        if (!serverProc.HasExited)
            serverProc.Kill(true);
        DeletePid();
        Console.WriteLine("Done.");
    }
```

- [ ] **Step 5: Build and verify no compile errors**

```bash
dotnet build --no-incremental
```

Expected: Succeeded

- [ ] **Step 6: Run all existing tests to verify nothing is broken**

```bash
dotnet test tests/Veron.Tests.csproj -v minimal --no-build
```

Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add Program.cs
git commit -m "feat(tool-config): wire TOOL blocks into CmdClaude

Parse TOOL claude-code config from modelfile and pass parameters as CLI
flags to 'claude code'. Validate known params, pass through unknowns."
```

---

### Task 6: Integrate TOOL Validation into `veron create`

**Files:**
- Modify: `Program.cs:479-580` (ValidateModelfile method)

> **Approach:** After the existing PARAMETER validation in ValidateModelfile, also parse and validate TOOL blocks. Use the same validation rules — known params validated, unknowns pass through.

- [ ] **Step 1: Write the failing test for create integration**

Add to `tests/ToolParsingTests.cs`:

```csharp
    [Fact]
    public void ValidateModelfile_Rejects_Invalid_Tool_Param()
    {
        var content = @"FROM MiniCPM5-1B-Q4_K_M.gguf
TOOL claude-code
  PARAMETER effort invalid-value
END_TOOL";
        using var tmp = CreateTempFile(content);
        // This should produce errors because 'invalid-value' is not a valid effort level
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
        // Unknown params pass through — no error
        var errors = ProgramTestHelper.ValidateToolBlocks(tmp.Path, "test-name", "/home/genkop/Workspace/llama-cpp/models");
        Assert.Empty(errors);
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
```

- [ ] **Step 2: Add the ValidateToolBlocks method to Program.cs**

Add this after `ValidateModelfile` (around line 580):

```csharp
    static List<string> ValidateToolBlocks(string sourcePath, string modelsDir)
    {
        var errors = new List<string>();

        // Parse TOOL blocks — this will throw on structural errors (END_TOOL without TOOL, etc.)
        Dictionary<string, ToolConfig>? toolConfigs;
        try
        {
            toolConfigs = ParseToolBlocks(sourcePath);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
            return errors;
        }

        // Validate parameters in each TOOL block
        foreach (var (_, toolCfg) in toolConfigs)
        {
            foreach (var (key, value) in toolCfg.Parameters)
            {
                var paramErrors = ValidateClaudeCodeParameter(key, value);
                if (paramErrors.Count > 0)
                {
                    errors.Add($"Error in TOOL \"{toolCfg.Name}\": {paramErrors[0]}");
                    return errors;
                }
            }
        }

        return errors;
    }
```

- [ ] **Step 3: Wire ValidateToolBlocks into the existing ValidateModelfile**

In `ValidateModelfile` (line 479), after the PARAMETER validation loop (around line 576, just before `return errors;`), add:

```csharp
        // ── Validate TOOL blocks ────────────────────────────────
        var toolErrors = ValidateToolBlocks(sourcePath, modelsDir);
        errors.AddRange(toolErrors);

        return errors;
```

- [ ] **Step 4: Expose ValidateToolBlocks via ProgramTestHelper**

Update `ProgramTestHelper`:

```csharp
    public static class ProgramTestHelper
    {
        public static Dictionary<string, ToolConfig> ParseToolBlocks(string path) =>
            Program.ParseToolBlocks(path);

        public static List<string> ValidateClaudeCodeParameter(string key, string value) =>
            Program.ValidateClaudeCodeParameter(key, value);

        public static List<string> ValidateToolBlocks(string sourcePath, string name, string modelsDir) =>
            Program.ValidateToolBlocks(sourcePath, modelsDir);
    }
```

- [ ] **Step 5: Run all tests**

```bash
dotnet test tests/Veron.Tests.csproj -v minimal --no-build
```

Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add Program.cs tests/ToolParsingTests.cs
git commit -m "feat(tool-config): integrate TOOL validation into veron create"
```

---

### Task 7: Update Usage Text

**Files:**
- Modify: `Program.cs:726-791` (PrintUsage method)

- [ ] **Step 1: Add TOOL blocks documentation to PrintUsage**

In the modelfile format section of PrintUsage, after the existing PARAMETER examples, add:

```
TOOL claude-code
  PARAMETER permission-mode auto
  PARAMETER tools Bash,Edit,Read,Write
  PARAMETER append-system-prompt ""You are working with a local model""
  PARAMETER effort high
END_TOOL

TOOL parameters for claude-code:
  permission-mode     Permission mode (auto, plan, dontAsk, bypassPermissions, default, acceptEdits)
  tools               Comma-separated list of allowed tools
  disallowedTools     Comma-separated list of denied tools
  allowedTools        Comma-separated tools that don't need prompting
  append-system-prompt Append text to the system prompt
  effort              Effort level (low, medium, high, xhigh, max)
  max-budget-usd      Max dollar amount (print mode)
  max-turns           Agentic turn limit (print mode)

Unknown TOOL parameters are passed through.
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build --no-incremental
```

Expected: Succeeded

- [ ] **Step 3: Commit**

```bash
git add Program.cs
git commit -m "docs: add TOOL block documentation to usage text"
```

---

### Task 8: End-to-End Smoke Test

**Files:**
- No code changes — manual verification only

- [ ] **Step 1: Build the release binary**

```bash
cd /home/genkop/Workspace/llama-cpp/Veron-AI-Serve-Cli
dotnet publish -c Release -o bin/release
```

- [ ] **Step 2: Verify help text shows TOOL documentation**

```bash
./bin/release/veron h | grep -A5 "TOOL"
```

Expected: Output shows TOOL block documentation

- [ ] **Step 3: Test with existing modelfile that has no TOOL blocks (backward compat)**

```bash
./bin/release/veron ls
```

Expected: Lists profiles without errors

- [ ] **Step 4: Create a test modelfile with TOOL blocks and validate via create**

Create `/tmp/test-tool-modelfile`:
```
FROM MiniCPM5-1B-Q4_K_M.gguf
PARAMETER port 5570
PARAMETER context 32000

TOOL claude-code
  PARAMETER permission-mode auto
  PARAMETER effort high
END_TOOL
```

Then run:
```bash
./bin/release/veron create test-tool /tmp/test-tool-modelfile
```

Expected: Profile created successfully with validation passing

- [ ] **Step 5: Test validation rejection — invalid effort value**

Create `/tmp/test-bad-tool-modelfile`:
```
FROM MiniCPM5-1B-Q4_K_M.gguf
PARAMETER port 5570

TOOL claude-code
  PARAMETER effort invalid-level
END_TOOL
```

Then run:
```bash
./bin/release/veron create test-bad-tool /tmp/test-bad-tool-modelfile
```

Expected: Error message about invalid effort value, exit code 1

- [ ] **Step 6: Clean up test profiles**

```bash
rm -f ~/.veron/modelfiles/test-tool ~/.veron/modelfiles/test-bad-tool
```

---

### Task 9: Final Test Run and Commit

- [ ] **Step 1: Run all tests one final time**

```bash
dotnet test tests/Veron.Tests.csproj -v minimal
```

Expected: All PASS

- [ ] **Step 2: Verify the build is clean**

```bash
dotnet build -c Release --no-incremental
```

Expected: Succeeded, no warnings

- [ ] **Step 3: Final commit**

```bash
git add .
git commit -m "feat(tool-config): add TOOL block support for modelfile tool configuration

- Parse TOOL/END_TOOL blocks from modelfiles
- Validate Claude Code parameters (permission-mode, effort, max-turns, etc.)
- Pass unknown parameters through without validation
- Wire into CmdClaude to pass TOOL claude-code params as CLI flags
- Integrate validation into veron create command
- Update usage text with TOOL block documentation

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
