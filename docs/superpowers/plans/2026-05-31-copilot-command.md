# Copilot Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `veron copilot <name>` that starts llama-server and launches GitHub Copilot CLI against it using BYOK mode.

**Architecture:** Mirror the existing `CmdClaude` pattern — server management, TOOL block parsing (`TOOL copilot`), env var injection (BYOK vars), auto-stop on exit. New `CopilotValidator` mirrors `ClaudeCodeValidator`.

**Tech Stack:** .NET 8, C#, xUnit

---

### Task 1: CopilotValidator — Enum Parameter Tests and Implementation

**Files:**
- Create: `Veron/Validation/CopilotValidator.cs`
- Test: `tests/CopilotCommandTests.cs` (validator test section)

- [ ] **Step 1: Write the failing tests for CopilotValidator enum parameters**

Create `tests/CopilotCommandTests.cs`:

```csharp
using System;
using Veron;
using Xunit;

namespace Veron.Tests;

public class CopilotCommandTests
{
    // ── Validator Tests ─────────────────────────────────────────────

    [Fact]
    public void Validator_Effort_ValidValues_Pass()
    {
        foreach (var val in new[] { "none", "low", "medium", "high", "xhigh", "max" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("effort", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_Effort_InvalidValue_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("effort", "ultra");
        Assert.NotEmpty(errors);
        Assert.Contains("invalid value", errors[0]);
    }

    [Fact]
    public void Validator_Mode_ValidValues_Pass()
    {
        foreach (var val in new[] { "interactive", "plan", "autopilot" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("mode", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_Mode_InvalidValue_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("mode", "custom");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validator_LogLevel_ValidValues_Pass()
    {
        foreach (var val in new[] { "none", "error", "warning", "info", "debug", "all", "default" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("log-level", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_LogLevel_InvalidValue_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("log-level", "verbose");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validator_Stream_ValidValues_Pass()
    {
        foreach (var val in new[] { "on", "off" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("stream", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_Stream_InvalidValue_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("stream", "yes");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validator_OutputFormat_ValidValues_Pass()
    {
        foreach (var val in new[] { "text", "json" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("output-format", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_BashEnv_ValidValues_Pass()
    {
        foreach (var val in new[] { "on", "off" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("bash-env", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_Mouse_ValidValues_Pass()
    {
        foreach (var val in new[] { "on", "off" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("mouse", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_ReasoningEffort_Alias_For_Effort()
    {
        // reasoning-effort is an alias for effort — same valid values
        var errors = CopilotValidator.ValidateCopilotParameter("reasoning-effort", "high");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validator_MaxAutopilotContinues_Integer_Pass()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("max-autopilot-continues", "10");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validator_MaxAutopilotContinues_NonInteger_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("max-autopilot-continues", "ten");
        Assert.NotEmpty(errors);
        Assert.Contains("expected an integer", errors[0]);
    }

    [Fact]
    public void Validator_UnknownParam_PassesThrough()
    {
        // Unknown parameters should pass through without validation
        var errors = CopilotValidator.ValidateCopilotParameter("some-future-flag", "anything");
        Assert.Empty(errors);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~CopilotCommandTests" --no-build 2>&1 || true
```

Expected: All validator tests fail with CS0103 (name 'CopilotValidator' does not exist) or similar compilation errors.

- [ ] **Step 3: Write CopilotValidator implementation**

Create `Veron/Validation/CopilotValidator.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Veron;

static class CopilotValidator
{
    static readonly Dictionary<string, HashSet<string>> KnownModes = new()
    {
        ["effort"]             = new() { "none", "low", "medium", "high", "xhigh", "max" },
        ["reasoning-effort"]   = new() { "none", "low", "medium", "high", "xhigh", "max" },
        ["mode"]               = new() { "interactive", "plan", "autopilot" },
        ["log-level"]          = new() { "none", "error", "warning", "info", "debug", "all", "default" },
        ["stream"]             = new() { "on", "off" },
        ["output-format"]      = new() { "text", "json" },
        ["bash-env"]           = new() { "on", "off" },
        ["mouse"]              = new() { "on", "off" },
    };

    static readonly HashSet<string> KnownIntParams = new()
    {
        "max-autopilot-continues"
    };

    public static List<string> ValidateCopilotParameter(string key, string value)
    {
        var errors = new List<string>();

        // Check known enum parameters
        if (KnownModes.TryGetValue(key, out var validValues))
        {
            if (!validValues.Contains(value))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\". Valid values: {string.Join(", ", validValues)}");
                return errors;
            }
            return errors;
        }

        // Check known integer parameters
        if (KnownIntParams.Contains(key))
        {
            if (!int.TryParse(value, out _))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\" — expected an integer");
                return errors;
            }
            return errors;
        }

        // Unknown parameters pass through without validation
        return errors;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~CopilotCommandTests" -v normal
```

Expected: All 16 validator tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Veron/Validation/CopilotValidator.cs tests/CopilotCommandTests.cs
git commit -m "feat: add CopilotValidator for TOOL copilot parameter validation"
```

---

### Task 2: CmdCopilot — Command Implementation

**Files:**
- Create: `Veron/Commands/CmdCopilot.cs`

- [ ] **Step 1: Write CmdCopilot implementation**

Create `Veron/Commands/CmdCopilot.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Veron;

static class CmdCopilot
{
    const string DefaultCopilotBin = "copilot";

    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = ModelfileParser.LoadConfig(opts, modelsDir, out string? modelfilePath);

        // The model profile name the user typed
        string modelName = opts.GetValueOrDefault("model") ?? throw new ArgumentNullException("model argument required");

        // Check if server is already running for this model — reuse if so
        bool serverAlreadyRunning = StateManager.IsServerRunning(modelName);

        if (!serverAlreadyRunning)
        {
            var cmd = LlamaServer.BuildLlamaCmd(cfg);

            Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " ...");

            var psi = BuildBackgroundServerPsi(cmd);

            var serverProc = Process.Start(psi)
                            ?? throw new InvalidOperationException("Failed to start llama-server");

            // Start stream draining threads so the server doesn't block on full pipe buffers
            _ = Task.Run(() => { try { serverProc.StandardOutput.ReadToEnd(); } catch { } });
            _ = Task.Run(() => { try { serverProc.StandardError.ReadToEnd(); } catch { } });

            string baseUrl = "http://localhost:" + cfg.Port;

            if (!LlamaServer.WaitForServer(baseUrl, cfg.Wait))
            {
                Console.Error.WriteLine("Error: server did not respond within " + cfg.Wait + "s");
                Environment.Exit(1);
            }

            Console.WriteLine("Server is ready at " + baseUrl);

            // Write server state file
            string fromName = Path.GetFileNameWithoutExtension(cfg.ModelPath);
            var serverState = new ServerState
            {
                Model = modelName,
                From = fromName,
                Port = cfg.Port,
                Context = cfg.Context,
                Pid = serverProc.Id,
                StartedAt = DateTime.UtcNow
            };
            StateManager.WriteState(serverState);

            // Also write the legacy PID file for backward compatibility
            PidManager.WritePid(serverProc.Id);
        }
        else
        {
            var existing = StateManager.GetState(modelName);
            Console.WriteLine("Server for " + modelName + " is already running (PID " +
                existing!.Pid + ", port " + existing.Port + ") — reusing.");
        }

        string baseUrl2 = "http://localhost:" + cfg.Port;

        // Parse TOOL blocks from modelfile
        string toolName = "copilot";
        Dictionary<string, ToolConfig>? toolConfigs = null;

        if (modelfilePath is not null)
        {
            try { toolConfigs = ModelfileParser.ParseToolBlocks(modelfilePath); }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(1);
            }
        }

        // Build copilot CLI arguments
        var copilotArgs = new List<string>();

        // Pass the model alias so Copilot knows which model it's using
        copilotArgs.Add("--model");
        copilotArgs.Add(cfg.Alias);

        // Handle --prompt for non-interactive mode
        string? promptText = opts.GetValueOrDefault("prompt");
        if (promptText is not null)
        {
            copilotArgs.Add("-p");
            copilotArgs.Add(promptText);
        }

        // Apply TOOL copilot parameters from modelfile
        if (toolConfigs is not null && toolConfigs.TryGetValue(toolName, out var toolCfg))
        {
            foreach (var (key, value) in toolCfg.Parameters)
            {
                var valErrors = CopilotValidator.ValidateCopilotParameter(key, value);
                if (valErrors.Count > 0)
                {
                    Console.Error.WriteLine(valErrors[0]);
                    Environment.Exit(1);
                }

                copilotArgs.Add("--" + key);
                copilotArgs.Add(value);
            }

            Console.WriteLine("Using TOOL copilot config from modelfile");
        }

        // Launch copilot
        string copilotBin = opts.GetValueOrDefault("copilot-bin", DefaultCopilotBin);
        var copilotPsi = new ProcessStartInfo(copilotBin, string.Join(" ", copilotArgs.Select(CliParser.EscapeArg)))
        {
            UseShellExecute = false,
        };

        // Set BYOK environment variables
        copilotPsi.EnvironmentVariables["COPILOT_PROVIDER_TYPE"]     = "openai";
        copilotPsi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"] = baseUrl2 + "/v1";
        copilotPsi.EnvironmentVariables["COPILOT_MODEL"]             = cfg.Alias;
        copilotPsi.EnvironmentVariables["COPILOT_OFFLINE"]           = "true";

        Console.WriteLine();
        Console.WriteLine("Launching copilot ...");
        Console.WriteLine("  COPILOT_PROVIDER_TYPE     = openai");
        Console.WriteLine("  COPILOT_PROVIDER_BASE_URL = " + baseUrl2 + "/v1");
        Console.WriteLine("  COPILOT_MODEL             = " + cfg.Alias);
        Console.WriteLine("  COPILOT_OFFLINE           = true");

        if (promptText is not null)
            Console.WriteLine("  --prompt                    = " + promptText);

        if (toolConfigs is not null && toolConfigs.ContainsKey(toolName))
            Console.WriteLine("  TOOL copilot args         = " + string.Join(" ", copilotArgs.Skip(2)));

        Console.WriteLine();

        try
        {
            var copilotProc = Process.Start(copilotPsi)
                                 ?? throw new FileNotFoundException(copilotBin);
            copilotProc.WaitForExit();
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is Win32Exception)
        {
            Console.Error.WriteLine("Error: couldn't launch '" + copilotBin + "' — make sure it's in $PATH.");
            Environment.Exit(1);
        }

        // Stop the server if we started it (not if it was already running)
        if (!serverAlreadyRunning)
        {
            Console.WriteLine();
            Console.WriteLine("Copilot exited — stopping llama-server for " + modelName + " ...");
            StateManager.StopServer(modelName);
            PidManager.DeletePid();
        }
    }

    /// <summary>
    /// Builds a ProcessStartInfo for running llama-server as a detached background process.
    /// All streams are redirected so the server output doesn't clutter the veron console.
    /// </summary>
    static ProcessStartInfo BuildBackgroundServerPsi(List<string> cmd)
    {
        return new ProcessStartInfo(cmd[0], string.Join(" ", cmd.Skip(1).Select(CliParser.EscapeArg)))
        {
            UseShellExecute   = false,
            CreateNoWindow    = true,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
dotnet build Veron/Veron.csproj -v minimal
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Veron/Commands/CmdCopilot.cs
git commit -m "feat: add CmdCopilot to start llama-server and launch copilot CLI via BYOK"
```

---

### Task 3: Wire Up Dispatch, Help, and --prompt in Program.cs

**Files:**
- Modify: `Veron/Program.cs`

- [ ] **Step 1: Add the copilot case to the dispatch switch**

In `Program.cs`, add this line after the `run` case (around line 51):

```csharp
case "copilot": CmdCopilot.Run(opts, modelsDir); break;
```

- [ ] **Step 2: Add copilot to the help text**

In the `PrintUsage()` method's COMMANDS section, add this line after the `run` entry:

```
  copilot <name>      Start llama-server then launch copilot (auto-stops server on exit)
```

- [ ] **Step 3: Add --prompt to the serve/claude options help text**

Add a new section after "SERVE / CLAUDE OPTIONS" in the help text:

```
COPILOT OPTIONS
  <name>               Modelfile name (without extension) in ~/.veron/modelfiles/
  --prompt <text>      Execute a prompt in non-interactive mode (exits after completion)
```

Also add the copilot entry to the examples section:

```
  veron copilot qwopus
  veron copilot qwopus --prompt "Fix the bug in main.js"
```

- [ ] **Step 4: Build to verify it compiles**

```bash
dotnet build Veron/Veron.csproj -v minimal
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Verify help output includes copilot**

```bash
dotnet run --project Veron/Veron.csproj -- h
```

Expected: `copilot <name>` appears in the COMMANDS section of the help output.

- [ ] **Step 6: Commit**

```bash
git add Veron/Program.cs
git commit -m "feat: wire up 'veron copilot' command in dispatch and help"
```

---

### Task 4: CmdCopilot Integration Tests

**Files:**
- Modify: `tests/CopilotCommandTests.cs` (add integration tests after validator section)

- [ ] **Step 1: Add server-state management tests for copilot**

Add these tests to the `CopilotCommandTests` class (after the validator tests, before the closing brace):

```csharp
    // ── Server State Management Tests ──────────────────────────────

    [Fact]
    public void StateManager_StopServer_Returns_False_For_Nonexistent_Copilot()
    {
        bool result = StateManager.StopServer("nonexistent-copilot-test");
        Assert.False(result);
    }

    [Fact]
    public void StateManager_WriteAndStop_Copilot_ServerState()
    {
        string modelName = "copilot-test-model";

        var state = new ServerState
        {
            Model = modelName,
            From = "Test.gguf",
            Port = 5570,
            Context = 4096,
            Pid = 99999999, // dead PID — StopServer will clean up and return false
            StartedAt = DateTime.UtcNow
        };

        StateManager.WriteState(state);

        // Dead PID means IsServerRunning returns false (and cleans up)
        Assert.False(StateManager.IsServerRunning(modelName));
    }

    [Fact]
    public void StateManager_DeleteState_Copilot_Removes_File()
    {
        string modelName = "copilot-delete-test";

        var state = new ServerState
        {
            Model = modelName,
            From = "Test.gguf",
            Port = 5571,
            Context = 4096,
            Pid = 1,
            StartedAt = DateTime.UtcNow
        };

        StateManager.WriteState(state);
        Assert.True(File.Exists(StateManager.StateFilePath(modelName)));

        StateManager.DeleteState(modelName);
        Assert.False(File.Exists(StateManager.StateFilePath(modelName)));
    }
```

- [ ] **Step 2: Run all CopilotCommandTests to verify they pass**

```bash
dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~CopilotCommandTests" -v normal
```

Expected: All 19 tests (16 validator + 3 server state) PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/CopilotCommandTests.cs
git commit -m "test: add CmdCopilot integration and server state tests"
```

---

### Task 5: README Documentation

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add copilot to the commands table**

In the Commands table, add a row after `claude`:

| `copilot <name>` | — | Start llama-server, set env vars, then launch `copilot`. Auto-stops when copilot exits |

- [ ] **Step 2: Add copilot to Quick Start examples**

Add these lines after the `veron run qwopus --prompt "Explain quantum computing"` line in the Quick Start section:

```bash
# Launch Copilot CLI with the server (auto-stops when done)
veron copilot qwopus

# One-shot prompt with copilot
veron copilot qwopus --prompt "Fix the bug in main.js"
```

- [ ] **Step 3: Add copilot to the examples section**

Add after the `veron claude qwopus` example:

```bash
# Launch Copilot CLI with a profile
veron copilot qwopus

# One-shot prompt with copilot
veron copilot qwopus --prompt "Fix the bug in main.js"
```

- [ ] **Step 4: Add Copilot TOOL block to the modelfile example**

In the Modelfile Format section, add a `TOOL copilot` example after the `TOOL claude-code` example:

```
# ── GitHub Copilot CLI configuration ───
TOOL copilot
  PARAMETER effort high
  PARAMETER mode interactive
  PARAMETER allow-tool Bash,Edit,Read,Write
  PARAMETER log-level info
END_TOOL
```

- [ ] **Step 5: Add a section describing the Copilot command**

Add after the "Environment Variables (set by `claude`)" section:

```markdown
## Environment Variables (set by `copilot`)

When you run `veron copilot`, it automatically sets:

```bash
export COPILOT_PROVIDER_TYPE="openai"
export COPILOT_PROVIDER_BASE_URL="http://localhost:<port>/v1"
export COPILOT_MODEL="<alias>"
export COPILOT_OFFLINE="true"
```

Then launches `copilot --model <alias>` with those env vars. TOOL block parameters are passed as CLI flags to the Copilot command.

**Supported Copilot CLI parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `effort` / `reasoning-effort` | string | Reasoning effort: `none`, `low`, `medium`, `high`, `xhigh`, `max` |
| `mode` | string | Agent mode: `interactive`, `plan`, `autopilot` |
| `log-level` | string | Log level: `none`, `error`, `warning`, `info`, `debug`, `all`, `default` |
| `stream` | string | Streaming mode: `on`, `off` |
| `output-format` | string | Output format: `text`, `json` |
| `bash-env` | string | BASH_ENV support: `on`, `off` |
| `mouse` | string | Mouse support: `on`, `off` |
| `max-autopilot-continues` | integer | Max continuation messages in autopilot mode |

Unknown parameters are passed through as-is, so future Copilot CLI flags work without needing Veron updates.
```

- [ ] **Step 6: Commit**

```bash
git add README.md
git commit -m "docs: add veron copilot command to README"
```

---

### Task 6: Full Test Run and Verification

**Files:**
- All project files

- [ ] **Step 1: Build the full solution in Release mode**

```bash
dotnet build Veron/Veron.csproj -c Release -v minimal
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Run all tests**

```bash
dotnet test tests/Veron.Tests.csproj -v normal
```

Expected: All tests pass (including existing Claude, Cat, and new Copilot tests).

- [ ] **Step 3: Verify help output shows copilot command**

```bash
dotnet run --project Veron/Veron.csproj -c Release -- h
```

Expected: `copilot <name>` appears in the COMMANDS section.

- [ ] **Step 4: Commit any final fixes (if needed)**

If any changes were made during verification, commit them:

```bash
git add -A
git commit -m "fix: address verification issues for copilot command"
```

---

## Self-Review Checklist

**Spec coverage:**
- [x] CmdCopilot implementation — Task 2
- [x] CopilotValidator — Task 1
- [x] BYOK env vars (COPILOT_PROVIDER_TYPE, BASE_URL, MODEL, OFFLINE) — Task 2
- [x] TOOL copilot block support — Task 2
- [x] --prompt flag for non-interactive mode — Tasks 2 + 3
- [x] Server reuse / auto-stop — Task 2
- [x] Program.cs dispatch and help — Task 3
- [x] Tests (validator, server state) — Tasks 1 + 4
- [x] README documentation — Task 5

**Placeholder scan:** No TBDs, TODOs, or vague descriptions. All code blocks contain complete implementations.

**Type consistency:** CopilotValidator uses `List<string>` return type matching ClaudeCodeValidator pattern. CmdCopilot uses same types as CmdClaude (Dictionary<string,string>, ModelConfig, ServerState). No cross-task naming conflicts.
