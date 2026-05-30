# `veron run` Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `veron run <name>` command that launches `llama-cli` for interactive chat with a model, with support for one-shot `--prompt` mode.

**Architecture:** New `LlamaCli.cs` helper (mirrors `LlamaServer.cs`), new `CmdRun.cs` command handler (mirrors `CmdServe.cs`). Four new fields on `ModelConfig`. No PID tracking, no health check — foreground process only.

**Tech Stack:** .NET 10, xUnit, C#

---

### Task 1: Add ModelConfig Fields for `run` Command

**Files:**
- Modify: `Veron/Models/ModelConfig.cs`
- Create: `tests/RunCommandTests.cs` (skeleton with one smoke test)

**Goal:** Add the four new fields (`Color`, `Temperature`, `TopP`, `Prompt`) to `ModelConfig` and verify the project still compiles.

- [ ] **Step 1: Write a smoke test that ModelConfig has the new fields**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd /home/genkop/Workspace/llama-cpp/Veron-AI-Serve-Cli
dotnet test --filter "FullyQualifiedName~ModelConfig_Has_Run_Command_Fields" -v normal
```

Expected: FAIL — `error CS1061` (does not contain a definition for `Color`, `Temperature`, `TopP`, `Prompt`)

- [ ] **Step 3: Add the four new fields to ModelConfig**

```csharp
namespace Veron;

class ModelConfig
{
    public string  ModelPath     { get; set; } = "";
    public string  Alias         { get; set; } = "";
    public int     Port          { get; set; } = 5570;
    public int     Context       { get; set; } = 128000;
    public bool    Jinja         { get; set; } = true;
    public bool    Fa            { get; set; } = true;
    public float   RepeatPenalty { get; set; } = 1.05f;
    public int?    NGpuLayers    { get; set; }
    public int?    BatchSize     { get; set; }
    public int     Wait          { get; set; } = 30;

    // New fields for 'run' command
    public bool?   Color         { get; set; } = true;
    public float?  Temperature   { get; set; }
    public float?  TopP          { get; set; }
    public string? Prompt        { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test --filter "FullyQualifiedName~ModelConfig_Has_Run_Command_Fields" -v normal
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Veron/Models/ModelConfig.cs tests/RunCommandTests.cs
git commit -m "feat: add run command fields to ModelConfig (Color, Temperature, TopP, Prompt)"
```

---

### Task 2: Create LlamaCli Helper with Tests

**Files:**
- Create: `Veron/Process/LlamaCli.cs`
- Modify: `Veron/Testing/ProgramTestHelper.cs` (expose BuildLlamaCmd for tests)
- Modify: `tests/RunCommandTests.cs` (add LlamaCli command-building tests)

**Goal:** Build the `llama-cli` argument list correctly from `ModelConfig`, with proper defaults.

- [ ] **Step 1: Write tests for LlamaCli.BuildLlamaCmd — default flags**

Add to `tests/RunCommandTests.cs`:

```csharp
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
        Assert.Contains("-m");
        Assert.Contains("--alias");
        Assert.Contains("-ngl");
        Assert.Contains("--flash-attn");
        Assert.Contains("--jinja");
        Assert.Contains("--color");
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

        Assert.Contains("--prompt");
        Assert.Contains("--single-turn");
        Assert.Contains("Hello world");
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

        Assert.DoesNotContain("--single-turn");
        Assert.DoesNotContain("--prompt");
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

        Assert.DoesNotContain("--color");
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

        Assert.Contains("--temperature");
        Assert.Contains("0.30");
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

        Assert.Contains("--top-p");
        Assert.Contains("0.50");
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

        Assert.DoesNotContain("--flash-attn");
    }
```

- [ ] **Step 2: Run tests to verify they fail (BuildLlamaCmd doesn't exist yet)**

```bash
dotnet test --filter "FullyQualifiedName~RunCommandTests" -v normal
```

Expected: FAIL — `error CS0117` (does not contain a definition for `BuildLlamaCmd`)

- [ ] **Step 3: Create LlamaCli.cs**

Create `Veron/Process/LlamaCli.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Veron;

static class LlamaCli
{
    const string DefaultBinary = "/home/genkop/Workspace/llama-cpp/llama.cpp/build/bin/llama-cli";

    public static List<string> BuildLlamaCmd(ModelConfig cfg)
    {
        var cmd = new List<string>
        {
            DefaultBinary,
            "-m", cfg.ModelPath,
            "--alias", cfg.Alias,
            "-ngl", (cfg.NGpuLayers ?? -1).ToString(),
            "-c", cfg.Context.ToString(),
            "--repeat-penalty", cfg.RepeatPenalty.ToString("0.00"),
        };

        if (cfg.Fa) cmd.Add("--flash-attn");
        if (cfg.Jinja) cmd.Add("--jinja");
        if (cfg.Color ?? true) cmd.Add("--color");

        if (cfg.Temperature.HasValue)
            cmd.AddRange(new[] { "--temperature", cfg.Temperature.Value.ToString("0.00") });

        if (cfg.TopP.HasValue)
            cmd.AddRange(new[] { "--top-p", cfg.TopP.Value.ToString("0.00") });

        if (cfg.Prompt is not null)
        {
            cmd.AddRange(new[] { "--prompt", cfg.Prompt });
            cmd.Add("--single-turn");
        }

        return cmd;
    }

    public static ProcessStartInfo CliPsi(List<string> cmd)
    {
        return new ProcessStartInfo(cmd[0], string.Join(" ", cmd.Skip(1).Select(CliParser.EscapeArg)))
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
    }
}
```

- [ ] **Step 4: Expose BuildLlamaCmd via ProgramTestHelper**

Add to `Veron/Testing/ProgramTestHelper.cs`:

```csharp
    public static List<string> BuildLlamaCmd(ModelConfig cfg) =>
        LlamaCli.BuildLlamaCmd(cfg);
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~RunCommandTests" -v normal
```

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Veron/Process/LlamaCli.cs Veron/Testing/ProgramTestHelper.cs tests/RunCommandTests.cs
git commit -m "feat: add LlamaCli helper with BuildLlamaCmd and tests"
```

---

### Task 3: Create CmdRun Command Handler

**Files:**
- Create: `Veron/Commands/CmdRun.cs`

**Goal:** Wire up the command handler that loads config, builds the command, and runs it as a foreground process.

- [ ] **Step 1: Create CmdRun.cs**

Create `Veron/Commands/CmdRun.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Veron;

static class CmdRun
{
    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = ModelfileParser.LoadConfig(opts, modelsDir, out _);

        var cmd = LlamaCli.BuildLlamaCmd(cfg);

        Console.WriteLine("Running llama-cli for " + cfg.Alias + " …");
        Console.WriteLine("  Command: " + string.Join(" ", cmd.Select(CliParser.EscapeArg)));

        var psi = LlamaCli.CliPsi(cmd);
        var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start llama-cli");

        try { proc.WaitForExit(); }
        catch (OperationCanceledException) { } // Ctrl+C
        finally
        {
            if (!proc.HasExited)
            {
                Console.WriteLine("\nShutting down …");
                proc.Kill(true);
                Console.WriteLine("Stopped.");
            }
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
cd /home/genkop/Workspace/llama-cpp/Veron-AI-Serve-Cli
dotnet build Veron/Veron.csproj
```

Expected: SUCCEED with no errors

- [ ] **Step 3: Commit**

```bash
git add Veron/Commands/CmdRun.cs
git commit -m "feat: add CmdRun command handler for llama-cli"
```

---

### Task 4: Wire Up Program.cs — Switch Case and CLI Flag Overlay

**Files:**
- Modify: `Veron/Program.cs` (switch case + usage text)
- Modify: `Veron/Parsing/ModelfileParser.cs` (overlay CLI flags for run-specific params)

**Goal:** Register the `run` command in the CLI and handle its specific flags.

- [ ] **Step 1: Add the `"run"` case to Program.Main switch**

In `Veron/Program.cs`, add this line after the `"claude"` case (line 32):

```csharp
            case "run":     CmdRun.Run(opts, modelsDir); break;
```

- [ ] **Step 2: Add CLI flag overlay for run-specific params in ModelfileParser.LoadConfig**

In `Veron/Parsing/ModelfileParser.cs`, add these lines at the end of `LoadConfig` (before `return cfg;`, around line 43):

```csharp
        // Run command specific flags
        if (CliParser.OptsBool(opts, "no-color"))                      cfg.Color = false;
        if (opts.TryGetValue("temperature", out var t))               cfg.Temperature = float.Parse(t);
        if (opts.TryGetValue("top-p", out var p))                     cfg.TopP = float.Parse(p);
        if (opts.TryGetValue("prompt", out var pr))                   cfg.Prompt = pr;
```

- [ ] **Step 3: Update usage text in Program.PrintUsage**

Replace the COMMANDS section in `PrintUsage()` with:

```csharp
COMMANDS
  ls, list            List all available modelfiles
  create <name> <path> Create a profile from a modelfile (validates first)
  serve <name>        Start llama-server with the given model profile (foreground)
  claude <name>       Start llama-server then launch claude code (auto-stop after)
  run <name>          Run llama-cli interactively with the given model profile
  stop                Stop a previously started llama-server
  h, help             Show this help message
  v, version          Show version information
```

And add a new RUN OPTIONS section after SERVE / CLAUDE OPTIONS:

```csharp
RUN OPTIONS
  <name>               Modelfile name (without extension) in ~/.veron/modelfiles/
  --n-gpu-layers <n>   GPU layers to offload (default: -1 = full)
  --flash-attention    Enable flash attention (default: on)
  --no-flash-attention Disable flash attention
  --jinja              Use Jinja template (default: on)
  --no-jinja           Disable Jinja template
  --color              Enable colored output (default: on)
  --no-color           Disable colored output
  --temperature <f>    Temperature (default: 0.8)
  --top-p <f>          Top-p sampling (default: 0.9)
  --repeat-penalty <f> Repeat penalty (default: 1.1)
  --context <n>        Context size
  --prompt <text>      One-shot prompt, exit after response
```

Add a `run` example to the EXAMPLES section:

```csharp
EXAMPLES
  veron ls
  veron serve my-model-small
  veron claude my-model-large --port 5571
  veron run my-model
  veron run my-model --prompt "Explain quantum computing"
  veron serve Qwopus3.6-27b-MTP
```

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build Veron/Veron.csproj
```

Expected: SUCCEED with no errors

- [ ] **Step 5: Smoke test — verify `veron run --help` doesn't crash**

```bash
cd /home/genkop/Workspace/llama-cpp/Veron-AI-Serve-Cli
dotnet run --project Veron/Veron.csproj -- help
```

Expected: usage text is printed, no crashes, and `run` appears in the COMMANDS list

- [ ] **Step 6: Commit**

```bash
git add Veron/Program.cs Veron/Parsing/ModelfileParser.cs
git commit -m "feat: wire up run command in Program.cs and CLI flag overlay"
```

---

### Task 5: Update README.md

**Files:**
- Modify: `README.md`

**Goal:** Document the new `run` command in the README.

- [ ] **Step 1: Add `veron run` to the command list in README.md**

Read `README.md`, find the section listing commands, and add a line for `run`:

```markdown
| `run <name>` | Run llama-cli interactively with the given model profile |
```

Add a short example:

```markdown
### Interactive chat with run

```bash
veron run my-model
veron run my-model --temperature 0.3
veron run my-model --prompt "Explain quantum computing"
```

The `run` command uses `llama-cli` (not `llama-server`) for direct interactive chat.
It starts with sensible defaults: full GPU offload (`-ngl -1`), flash attention, colored output, and Jinja template.

One-shot mode with `--prompt` runs a single prompt and exits.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: document veron run command in README"
```

---

### Task 6: Final Verification — All Tests Pass and Build Clean

**Files:** (none — verification step)

**Goal:** Confirm everything builds and tests pass together.

- [ ] **Step 1: Run all tests**

```bash
cd /home/genkop/Workspace/llama-cpp/Veron-AI-Serve-Cli
dotnet test -v normal
```

Expected: PASS — all existing tests plus the new RunCommandTests

- [ ] **Step 2: Build Release for good measure**

```bash
dotnet build Veron/Veron.csproj -c Release
```

Expected: SUCCEED with no errors or warnings

- [ ] **Step 3: Commit (if there are any uncommitted changes, otherwise skip)**

---

## Self-Review Checklist

### Spec Coverage

| Spec Requirement | Task | Status |
|---|---|---|
| New `LlamaCli.cs` helper | Task 2 | ✅ |
| New `CmdRun.cs` handler | Task 3 | ✅ |
| ModelConfig: Color, Temperature, TopP, Prompt | Task 1 | ✅ |
| CLI flag overlay for new params | Task 4 Step 2 | ✅ |
| Switch case in Program.cs | Task 4 Step 1 | ✅ |
| Usage text update | Task 4 Step 3 | ✅ |
| One-shot `--prompt` with `--single-turn` | Task 2 Step 1 (test), Task 3 (impl) | ✅ |
| Default `-ngl -1` | Task 2 (LlamaCli.BuildLlamaCmd) | ✅ |
| Default `--flash-attn`, `--color`, `--jinja` | Task 2 (LlamaCli.BuildLlamaCmd) | ✅ |
| README documentation | Task 5 | ✅ |
| No PID tracking, no health check | Task 3 (CmdRun — neither used) | ✅ |

### Placeholder Scan

- ❌ No "TBD", "TODO", or vague instructions found. Every step has exact code and commands.

### Type Consistency

- `LlamaCli.BuildLlamaCmd` returns `List<string>` — consistent with `LlamaServer.BuildLlamaCmd`
- `LlamaCli.CliPsi` returns `ProcessStartInfo` — consistent with `LlamaServer.ServerPsi`
- New ModelConfig fields: `bool? Color`, `float? Temperature`, `float? TopP`, `string? Prompt` — nullable types match the "optional" semantics (temperature/top-p/prompt are optional, color defaults to true but is nullable for explicit control)
- `ProgramTestHelper.BuildLlamaCmd` wraps `LlamaCli.BuildLlamaCmd` — consistent with existing helper pattern
