# Help Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Slim the top-level `veron --help` output and add per-command `veron <command> --help` for detail.

**Architecture:** New `CmdHelp` class holds a static dictionary of help data per command. `Program.Main` intercepts `--help` after parsing options and routes to `CmdHelp.Run(command)`. The existing `PrintUsage()` is replaced with a slim version.

**Tech Stack:** C#, .NET 8, xUnit

---

### File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `Veron/Commands/CmdHelp.cs` | Help data structure + per-command help rendering |
| Modify | `Veron/Program.cs:63-185` | Slim `PrintUsage()`, add subcommand `--help` dispatch |
| Create | `tests/HelpCommandTests.cs` | Tests for CmdHelp and dispatch integration |

---

### Task 1: Create CmdHelp with help data

**Files:**
- Create: `Veron/Commands/CmdHelp.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/HelpCommandTests.cs`:

```csharp
using Veron;
using Xunit;

namespace Veron.Tests;

public class HelpCommandTests
{
    [Fact]
    public void CmdHelp_Has_Entries_For_All_Commands()
    {
        var commands = new[] { "cat", "ls", "list", "create", "serve", "claude", "copilot", "run", "ps", "stop", "remove", "rm", "help", "version" };

        foreach (var cmd in commands)
        {
            Assert.NotNull(CmdHelp.Get(cmd));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~HelpCommandTests" -v normal`

Expected: FAIL — `CmdHelp` type does not exist yet.

- [ ] **Step 3: Write CmdHelp implementation**

Create `Veron/Commands/CmdHelp.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Veron;

static class CmdHelp
{
    // ── Help data per command ────────────────────────────────────────────────

    static readonly Dictionary<string, CommandHelp> Commands = new()
    {
        ["cat"] = new("cat", "Show raw modelfile content", "veron cat <name>", Array.Empty<string>()),
        ["ls"] = new("ls", "List all available modelfiles", "veron ls", Array.Empty<string>()),
        ["list"] = new("ls", "List all available modelfiles", "veron ls", Array.Empty<string>()),
        ["create"] = new("create", "Create a profile from a modelfile", "veron create <name> <path>", Array.Empty<string>()),
        ["serve"] = new("serve", "Start llama-server with the given model profile (foreground)", "veron serve <name> [options]", new[]
        {
            "--alias <name>       Alias for the server (overwrites modelfile)",
            "--port <n>           Port (default: 5570)",
            "--context <n>        Context size (default: 128000)",
            "--jinja              Use Jinja template (default: on)",
            "--no-jinja           Disable Jinja template",
            "--flash-attention    Enable flash attention (default: on)",
            "--no-flash-attention Disable flash attention",
            "--repeat-penalty <f> Repeat penalty (default: 1.05)",
            "--n-gpu-layers <n>   GPU layers to offload",
            "--batch-size <n>     Batch size",
            "--wait <n>           Seconds to wait for server readiness (default: 30)",
        }),
        ["claude"] = new("claude", "Start llama-server then launch claude code", "veron claude <name> [options]", new[]
        {
            "--alias <name>       Alias for the server (overwrites modelfile)",
            "--port <n>           Port (default: 5570)",
            "--context <n>        Context size (default: 128000)",
            "--jinja              Use Jinja template (default: on)",
            "--no-jinja           Disable Jinja template",
            "--flash-attention    Enable flash attention (default: on)",
            "--no-flash-attention Disable flash attention",
            "--repeat-penalty <f> Repeat penalty (default: 1.05)",
            "--n-gpu-layers <n>   GPU layers to offload",
            "--batch-size <n>     Batch size",
            "--wait <n>           Seconds to wait for server readiness (default: 30)",
            "--foreground         Start llama-server in a new terminal window",
        }),
        ["copilot"] = new("copilot", "Start llama-server then launch copilot", "veron copilot <name> [options]", new[]
        {
            "--alias <name>       Alias for the server (overwrites modelfile)",
            "--port <n>           Port (default: 5570)",
            "--context <n>        Context size (default: 128000)",
            "--jinja              Use Jinja template (default: on)",
            "--no-jinja           Disable Jinja template",
            "--flash-attention    Enable flash attention (default: on)",
            "--no-flash-attention Disable flash attention",
            "--repeat-penalty <f> Repeat penalty (default: 1.05)",
            "--n-gpu-layers <n>   GPU layers to offload",
            "--batch-size <n>     Batch size",
            "--wait <n>           Seconds to wait for server readiness (default: 30)",
            "--prompt <text>      Execute a prompt in non-interactive mode (exits after completion)",
        }),
        ["run"] = new("run", "Run llama-cli interactively with the given model profile", "veron run <name> [options]", new[]
        {
            "--n-gpu-layers <n>   GPU layers to offload (default: -1 = full)",
            "--flash-attention    Enable flash attention (default: on)",
            "--no-flash-attention Disable flash attention",
            "--jinja              Use Jinja template (default: on)",
            "--no-jinja           Disable Jinja template",
            "--color              Enable colored output (default: on)",
            "--no-color           Disable colored output",
            "--temperature <f>    Temperature (default: 0.8)",
            "--top-p <f>          Top-p sampling (default: 0.9)",
            "--repeat-penalty <f> Repeat penalty (default: 1.1)",
            "--context <n>        Context size",
            "--prompt <text>      One-shot prompt, exit after response",
        }),
        ["ps"] = new("ps", "List currently running servers", "veron ps", Array.Empty<string>()),
        ["stop"] = new("stop", "Stop a specific server, or all if no name given", "veron stop [name]", Array.Empty<string>()),
        ["remove"] = new("remove", "Remove a model profile (stops server if running)", "veron remove <name>", new[]
        {
            "-f, --force       Skip confirmation prompt",
        }),
        ["rm"] = new("remove", "Remove a model profile (stops server if running)", "veron remove <name>", new[]
        {
            "-f, --force       Skip confirmation prompt",
        }),
        ["help"] = new("help", "Show this help message", "veron help", Array.Empty<string>()),
        ["version"] = new("version", "Show version information", "veron version", Array.Empty<string>()),
    };

    // Canonical command list for top-level help display (with aliases shown)
    static readonly (string Display, string Description)[] TopLevelCommands =
    [
        ("cat <name>", "Show raw modelfile content"),
        ("ls, list", "List all available modelfiles"),
        ("create <name> <path>", "Create a profile from a modelfile"),
        ("serve <name>", "Start llama-server with the given model profile (foreground)"),
        ("claude <name>", "Start llama-server then launch claude code"),
        ("copilot <name>", "Start llama-server then launch copilot"),
        ("run <name>", "Run llama-cli interactively with the given model profile"),
        ("ps", "List currently running servers"),
        ("stop [name]", "Stop a specific server, or all if no name given"),
        ("remove, rm <name>", "Remove a model profile"),
        ("help", "Show this help message"),
        ("version", "Show version information"),
    ];

    // ── Public API ───────────────────────────────────────────────────────────

    public static CommandHelp? Get(string commandName) => Commands.GetValueOrDefault(commandName);

    public static string[] TopLevelCommandNames() => Commands.Keys.ToArray();

    /// <summary>Print per-command help for the given command, or top-level help if null.</summary>
    public static void Run(string? commandName)
    {
        if (commandName is null)
        { PrintTopLevel(); return; }

        var entry = Get(commandName);
        if (entry is null)
        {
            Console.Error.WriteLine($"Unknown command: {commandName}");
            PrintTopLevel();
            Environment.Exit(1);
            return;
        }

        PrintCommand(entry);
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    static void PrintCommand(CommandHelp entry)
    {
        Console.WriteLine($"USAGE");
        Console.WriteLine($"  {entry.Usage}");

        if (entry.Options.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("OPTIONS");
            foreach (var opt in entry.Options)
                Console.WriteLine($"  {opt}");
        }
    }

    static void PrintTopLevel()
    {
        Console.WriteLine(@"
USAGE
  veron <command> [options]

COMMANDS".TrimStart());

        // Align descriptions at column 30
        int maxDisplay = TopLevelCommands.Max(c => c.Display.Length);
        foreach (var (display, desc) in TopLevelCommands)
        {
            Console.WriteLine($"  {display.PadRight(maxDisplay + 2)}{desc}");
        }

        Console.WriteLine(@"
GLOBAL OPTIONS
  --models-dir <dir>   Directory containing GGUF files

For more on a command: veron <command> --help".TrimStart());
    }
}

/// <summary>Help data for a single command.</summary>
readonly struct CommandHelp(string displayName, string description, string usage, string[] options)
{
    public string DisplayName => displayName;
    public string Description => description;
    public string Usage => usage;
    public string[] Options => options;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~HelpCommandTests" -v normal`

Expected: PASS — all 14 command entries resolve.

- [ ] **Step 5: Commit**

```bash
git add Veron/Commands/CmdHelp.cs tests/HelpCommandTests.cs
git commit -m "feat: add CmdHelp with per-command help data"
```

---

### Task 2: Add tests for CmdHelp output

**Files:**
- Modify: `tests/HelpCommandTests.cs`

- [ ] **Step 1: Add tests for help output content**

Update `tests/HelpCommandTests.cs`:

```csharp
using System;
using System.IO;
using Veron;
using Xunit;

namespace Veron.Tests;

public class HelpCommandTests
{
    [Fact]
    public void CmdHelp_Has_Entries_For_All_Commands()
    {
        var commands = new[] { "cat", "ls", "list", "create", "serve", "claude", "copilot", "run", "ps", "stop", "remove", "rm", "help", "version" };

        foreach (var cmd in commands)
        {
            Assert.NotNull(CmdHelp.Get(cmd));
        }
    }

    [Fact]
    public void CmdHelp_Serve_Includes_Options()
    {
        var entry = CmdHelp.Get("serve");
        Assert.NotNull(entry);

        var options = entry!.Value.Options;
        Assert.Contains(options, o => o.StartsWith("--port"));
        Assert.Contains(options, o => o.StartsWith("--context"));
        Assert.Contains(options, o => o.StartsWith("--alias"));
    }

    [Fact]
    public void CmdHelp_Ps_Has_No_Options()
    {
        var entry = CmdHelp.Get("ps");
        Assert.NotNull(entry);
        Assert.Empty(entry!.Value.Options);
    }

    [Fact]
    public void CmdHelp_UnknownCommand_Returns_Null()
    {
        Assert.Null(CmdHelp.Get("foobar"));
    }

    [Fact]
    public void CmdHelp_Run_Prints_Command_Help_To_Stdout()
    {
        using var sw = new StringWriter();
        Console.SetOut(sw);

        CmdHelp.Run("ps");

        string output = sw.ToString();
        Assert.Contains("USAGE", output);
        Assert.Contains("veron ps", output);
    }

    [Fact]
    public void CmdHelp_Run_Null_Prints_TopLevel()
    {
        using var sw = new StringWriter();
        Console.SetOut(sw);

        CmdHelp.Run(null);

        string output = sw.ToString();
        Assert.Contains("COMMANDS", output);
        Assert.Contains("--models-dir", output);
        Assert.Contains("veron <command> --help", output);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~HelpCommandTests" -v normal`

Expected: PASS — all 6 tests pass.

- [ ] **Step 3: Commit**

```bash
git add tests/HelpCommandTests.cs
git commit -m "test: add CmdHelp output content tests"
```

---

### Task 3: Slim PrintUsage() in Program.cs

**Files:**
- Modify: `Veron/Program.cs:63-185`

- [ ] **Step 1: Replace PrintUsage() with slim version**

Replace the entire `PrintUsage()` method (lines 63–177) in `Veron/Program.cs` with a call to `CmdHelp.Run(null)`:

```csharp
static void PrintUsage() => CmdHelp.Run(null);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Veron/Veron.csproj`

Expected: SUCCESS — no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Veron/Program.cs
git commit -m "refactor: replace PrintUsage with CmdHelp.Run(null)"
```

---

### Task 4: Add subcommand --help dispatch in Program.Main

**Files:**
- Modify: `Veron/Program.cs:11-59`

- [ ] **Step 1: Add subcommand --help detection before the switch statement**

Insert after the `--force` validation block (around line 36) and before the `switch (command)` (line 38):

```csharp
// Check for per-command --help
bool hasHelp = false;
foreach (var arg in rawArgs.Skip(1))
{
    if (arg == "--help" || arg == "-h") { hasHelp = true; break; }
}

if (hasHelp)
{
    CmdHelp.Run(command);
    return;
}
```

The full flow around line 36–58 should look like:

```csharp
// Validate --force/-f is only used with remove command
if (command != "remove" && command != "rm")
{
    bool hasForce = false;
    foreach (var arg in rawArgs.Skip(1))
    {
        if (arg == "-f" || arg == "--force") { hasForce = true; break; }
    }

    if (hasForce)
    {
        Console.Error.WriteLine("Error: flag --force/-f is only valid with the remove command");
        Environment.Exit(1);
    }
}

// Check for per-command --help
bool hasHelp = false;
foreach (var arg in rawArgs.Skip(1))
{
    if (arg == "--help" || arg == "-h") { hasHelp = true; break; }
}

if (hasHelp)
{
    CmdHelp.Run(command);
    return;
}

switch (command)
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Veron/Veron.csproj`

Expected: SUCCESS — no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Veron/Program.cs
git commit -m "feat: dispatch subcommand --help to CmdHelp.Run"
```

---

### Task 3: Integration test for subcommand --help dispatch

**Files:**
- Modify: `tests/HelpCommandTests.cs`

- [ ] **Step 1: Add integration test for top-level help content**

Add this test to `tests/HelpCommandTests.cs`:

```csharp
[Fact]
public void CmdHelp_TopLevel_Contains_All_Commands()
{
    using var sw = new StringWriter();
    Console.SetOut(sw);

    CmdHelp.Run(null);

    string output = sw.ToString();

    // Verify all commands appear
    Assert.Contains("cat", output);
    Assert.Contains("ls", output);
    Assert.Contains("create", output);
    Assert.Contains("serve", output);
    Assert.Contains("claude", output);
    Assert.Contains("copilot", output);
    Assert.Contains("run", output);
    Assert.Contains("ps", output);
    Assert.Contains("stop", output);
    Assert.Contains("remove", output);
    Assert.Contains("version", output);

    // Verify footer is present
    Assert.Contains("veron <command> --help", output);

    // Verify per-command options are NOT in top-level help
    Assert.DoesNotContain("--port", output);
    Assert.DoesNotContain("--context", output);
    Assert.DoesNotContain("--prompt", output);
}

[Fact]
public void CmdHelp_Run_Serve_Shows_Options()
{
    using var sw = new StringWriter();
    Console.SetOut(sw);

    CmdHelp.Run("serve");

    string output = sw.ToString();
    Assert.Contains("--port", output);
    Assert.Contains("--context", output);
    Assert.Contains("--alias", output);
}
```

- [ ] **Step 2: Run all HelpCommandTests**

Run: `dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~HelpCommandTests" -v normal`

Expected: PASS — all 8 tests pass.

- [ ] **Step 3: Run full test suite to ensure nothing is broken**

Run: `dotnet test -v normal`

Expected: PASS — all existing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add tests/HelpCommandTests.cs
git commit -m "test: add integration tests for help restructure"
```

---

### Task 4: End-to-end smoke test

**Files:**
- No new files — verify the built binary

- [ ] **Step 1: Publish and run top-level --help**

Run: `dotnet run --project Veron/Veron.csproj -- --help`

Verify the output shows the slim help with all commands listed but no per-command options.

- [ ] **Step 2: Run subcommand --help**

Run: `dotnet run --project Veron/Veron.csproj -- serve --help`

Verify the output shows USAGE and OPTIONS for serve.

- [ ] **Step 3: Run a no-options subcommand --help**

Run: `dotnet run --project Veron/Veron.csproj -- ps --help`

Verify the output shows just USAGE and description, no OPTIONS section.

- [ ] **Step 4: Run unknown command --help**

Run: `dotnet run --project Veron/Veron.csproj -- foobar --help`

Verify it prints "Unknown command" to stderr and shows top-level help.

---

## Self-Review Checklist

**Spec coverage:**
- Slim top-level help with all 12 commands listed → Task 3 (replace PrintUsage) ✓
- Footer "veron <command> --help" → CmdHelp.PrintTopLevel() ✓
- Subcommand --help for commands with options → Task 4 (dispatch) + CmdHelp.Run() ✓
- Subcommand --help for commands without options → CmdHelp.Run() shows USAGE only ✓
- Global options (--models-dir) retained in top-level → PrintTopLevel() ✓
- Per-command option lists removed from top-level → PrintTopLevel() has no per-command options ✓

**Placeholder scan:** No TBDs, TODOs, or vague instructions. All code blocks contain complete content. ✓

**Type consistency:** `CommandHelp` struct fields match usage in `CmdHelp.Get()`, `PrintCommand()`. Dispatch uses same `CmdHelp.Run(string?)` signature as top-level. ✓
