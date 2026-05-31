# Bare Flag Tool Parameters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow bare CLI flags (like `--yolo`) in TOOL block PARAMETER lines without a value, so modelfiles can produce flags like `--yolo` instead of only `--key value` pairs.

**Architecture:** Change `ToolConfig.Parameters` from `Dictionary<string, string>` to `Dictionary<string, string?>`. A `null` value means bare flag — emit only `--key`. Update the parser to store `null` when no value follows the key, and update consumers (CmdCopilot, CmdClaude, validators) to check for null before emitting a value.

**Tech Stack:** C# 12, .NET 8/10, xUnit

---

### Task 1: Add Tests for Bare Flag Parsing

**Files:**
- Test: `tests/ToolParsingTests.cs`

- [ ] **Step 1: Write failing test — bare flag parses as null**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ToolParsingTests.cs --filter "ParseToolBlocks_Bare_Flag_Stores_Null" -v normal`

Expected: FAIL — the parser currently skips lines with no value (`spaceIdx < 0 → continue`), so `"yolo"` won't be in the dictionary at all. The `Assert.Null(...)` will throw a `KeyNotFoundException`.

- [ ] **Step 3: Commit** (tests only, confirming they fail)

```bash
git add tests/ToolParsingTests.cs
git commit -m "test: add bare flag parsing test"
```

---

### Task 2: Change ToolConfig Parameters to Nullable Values

**Files:**
- Modify: `Veron/Models/ToolConfig.cs`

- [ ] **Step 1: Change Dictionary<string, string> to Dictionary<string, string?>**

```csharp
using System.Collections.Generic;

namespace Veron;

public class ToolConfig
{
    public string Name                           { get; set; } = "";
    public Dictionary<string, string?> Parameters { get; set; } = new();
}
```

- [ ] **Step 2: Run tests to verify nothing broke**

Run: `dotnet test -v normal`

Expected: All existing tests pass. The nullable change is backward compatible — all existing code assigns non-null strings.

- [ ] **Step 3: Commit**

```bash
git add Veron/Models/ToolConfig.cs
git commit -m "refactor: make ToolConfig.Parameters values nullable to support bare flags"
```

---

### Task 3: Update ParseToolBlocks to Store Null for Bare Flags

**Files:**
- Modify: `Veron/Parsing/ModelfileParser.cs` (lines 225-237 — the PARAMETER parsing inside TOOL blocks)

- [ ] **Step 1: Change `spaceIdx < 0` to store null instead of skipping**

In `ParseToolBlocks`, replace this block (around line 228):

```csharp
// BEFORE:
if (spaceIdx < 0) continue;
```

With:

```csharp
// AFTER:
if (spaceIdx < 0)
{
    string key = rest.Trim();
    result[currentToolName].Parameters[key] = null; // bare flag
    continue;
}
```

- [ ] **Step 2: Run the bare flag test to verify it passes**

Run: `dotnet test tests/ToolParsingTests.cs --filter "ParseToolBlocks_Bare_Flag_Stores_Null" -v normal`

Expected: PASS — `"yolo"` now maps to `null`, and `"effort"` still maps to `"high"`.

- [ ] **Step 3: Run all tests to verify nothing broke**

Run: `dotnet test -v normal`

Expected: All existing tests pass.

- [ ] **Step 4: Commit**

```bash
git add Veron/Parsing/ModelfileParser.cs
git commit -m "feat: parse bare PARAMETER flags as null values in TOOL blocks"
```

---

### Task 4: Update CmdCopilot to Emit Bare Flags Without Value

**Files:**
- Modify: `Veron/Commands/CmdCopilot.cs` (lines 107-118 — the TOOL parameter emission loop)

- [ ] **Step 1: Change the TOOL parameter loop to check for null**

Replace this block (around line 109):

```csharp
// BEFORE:
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
```

With:

```csharp
// AFTER:
foreach (var (key, value) in toolCfg.Parameters)
{
    var valErrors = CopilotValidator.ValidateCopilotParameter(key, value);
    if (valErrors.Count > 0)
    {
        Console.Error.WriteLine(valErrors[0]);
        Environment.Exit(1);
    }

    copilotArgs.Add("--" + key);
    if (value is not null)
        copilotArgs.Add(value);
}
```

- [ ] **Step 2: Run all tests to verify nothing broke**

Run: `dotnet test -v normal`

Expected: All existing tests pass.

- [ ] **Step 3: Commit**

```bash
git add Veron/Commands/CmdCopilot.cs
git commit -m "feat: emit bare flags without value in copilot command"
```

---

### Task 5: Update CmdClaude to Emit Bare Flags Without Value

**Files:**
- Modify: `Veron/Commands/CmdClaude.cs` (lines 130-142 — the TOOL parameter emission loop)

- [ ] **Step 1: Change the TOOL parameter loop to check for null**

Replace this block (around line 132):

```csharp
// BEFORE:
foreach (var (key, value) in toolCfg.Parameters)
{
    var valErrors = ClaudeCodeValidator.ValidateClaudeCodeParameter(key, value);
    if (valErrors.Count > 0)
    {
        Console.Error.WriteLine(valErrors[0]);
        Environment.Exit(1);
    }

    claudeArgs.Add("--" + key);
    claudeArgs.Add(value);
}
```

With:

```csharp
// AFTER:
foreach (var (key, value) in toolCfg.Parameters)
{
    var valErrors = ClaudeCodeValidator.ValidateClaudeCodeParameter(key, value);
    if (valErrors.Count > 0)
    {
        Console.Error.WriteLine(valErrors[0]);
        Environment.Exit(1);
    }

    claudeArgs.Add("--" + key);
    if (value is not null)
        claudeArgs.Add(value);
}
```

- [ ] **Step 2: Run all tests to verify nothing broke**

Run: `dotnet test -v normal`

Expected: All existing tests pass.

- [ ] **Step 3: Commit**

```bash
git add Veron/Commands/CmdClaude.cs
git commit -m "feat: emit bare flags without value in claude command"
```

---

### Task 6: Update CopilotValidator to Handle Null Values

**Files:**
- Modify: `Veron/Validation/CopilotValidator.cs`

- [ ] **Step 1: Add null check at the top of ValidateCopilotParameter**

Replace the method signature and add early return:

```csharp
// BEFORE:
public static List<string> ValidateCopilotParameter(string key, string value)
{
    var errors = new List<string>();
    // ... rest of method
}

// AFTER:
public static List<string> ValidateCopilotParameter(string key, string? value)
{
    if (value is null) return new(); // bare flag — nothing to validate

    var errors = new List<string>();
    // ... rest of method unchanged
}
```

- [ ] **Step 2: Add test for null value validation**

In `tests/CopilotCommandTests.cs`, add:

```csharp
[Fact]
public void Validator_NullValue_BareFlag_Passes()
{
    var errors = CopilotValidator.ValidateCopilotParameter("yolo", null);
    Assert.Empty(errors);
}
```

- [ ] **Step 3: Run the new test**

Run: `dotnet test tests/CopilotCommandTests.cs --filter "Validator_NullValue_BareFlag_Passes" -v normal`

Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add Veron/Validation/CopilotValidator.cs tests/CopilotCommandTests.cs
git commit -m "feat: copilot validator handles null bare flag values"
```

---

### Task 7: Update ClaudeCodeValidator to Handle Null Values

**Files:**
- Modify: `Veron/Validation/ClaudeCodeValidator.cs`

- [ ] **Step 1: Add null check at the top of ValidateClaudeCodeParameter**

Replace the method signature and add early return:

```csharp
// BEFORE:
public static List<string> ValidateClaudeCodeParameter(string key, string value)
{
    var errors = new List<string>();
    // ... rest of method
}

// AFTER:
public static List<string> ValidateClaudeCodeParameter(string key, string? value)
{
    if (value is null) return new(); // bare flag — nothing to validate

    var errors = new List<string>();
    // ... rest of method unchanged
}
```

- [ ] **Step 2: Add test for null value validation**

In `tests/ToolParsingTests.cs`, add:

```csharp
[Fact]
public void ValidateClaudeCodeParameter_NullValue_BareFlag_Passes()
{
    var errors = ProgramTestHelper.ValidateClaudeCodeParameter("yolo", null);
    Assert.Empty(errors);
}
```

- [ ] **Step 3: Run the new test**

Run: `dotnet test tests/ToolParsingTests.cs --filter "ValidateClaudeCodeParameter_NullValue_BareFlag_Passes" -v normal`

Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add Veron/Validation/ClaudeCodeValidator.cs tests/ToolParsingTests.cs
git commit -m "feat: claude code validator handles null bare flag values"
```

---

### Task 8: Update ProgramTestHelper for Nullable Support

**Files:**
- Modify: `Veron/Testing/ProgramTestHelper.cs`

- [ ] **Step 1: Change ValidateClaudeCodeParameter to accept nullable value**

```csharp
// BEFORE:
public static List<string> ValidateClaudeCodeParameter(string key, string value) =>
    ClaudeCodeValidator.ValidateClaudeCodeParameter(key, value);

// AFTER:
public static List<string> ValidateClaudeCodeParameter(string key, string? value) =>
    ClaudeCodeValidator.ValidateClaudeCodeParameter(key, value);
```

- [ ] **Step 2: Run all tests to verify nothing broke**

Run: `dotnet test -v normal`

Expected: All existing tests pass.

- [ ] **Step 3: Commit**

```bash
git add Veron/Testing/ProgramTestHelper.cs
git commit -m "refactor: make ProgramTestHelper.ValidateClaudeCodeParameter accept nullable value"
```

---

### Task 9: Add End-to-End Bare Flag Validation Test

**Files:**
- Test: `tests/ToolParsingTests.cs`

- [ ] **Step 1: Write test — bare flag passes modelfile validation**

```csharp
[Fact]
public void ValidateModelfile_Accepts_Bare_Flag_Param()
{
    var content = @"FROM MiniCPM5-1B-Q4_K_M.gguf
TOOL copilot
  PARAMETER yolo
  PARAMETER effort high
END_TOOL";
    using var tmp = CreateTempFile(content);
    var errors = ProgramTestHelper.ValidateToolBlocks(tmp.Path, "test-name", "/home/genkop/Workspace/llama-cpp/models");
    Assert.Empty(errors);
}
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test tests/ToolParsingTests.cs --filter "ValidateModelfile_Accepts_Bare_Flag_Param" -v normal`

Expected: PASS — bare flag is valid; effort=high is also valid.

- [ ] **Step 3: Commit**

```bash
git add tests/ToolParsingTests.cs
git commit -m "test: add end-to-end bare flag validation test"
```

---

### Task 10: Full Regression Test Run

**Files:**
- All test files

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test -v normal`

Expected: All tests pass, including existing tests (backward compatibility) and new bare flag tests.

- [ ] **Step 2: If any tests fail, fix them inline before continuing**

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "fix: resolve any remaining issues from bare flag support"
```

(Only if there are changes; otherwise skip this commit.)

---

## Self-Review

**Spec coverage:**
- [x] ToolConfig model → Task 2
- [x] ParseToolBlocks → Task 3
- [x] CmdCopilot → Task 4
- [x] CmdClaude → Task 5
- [x] Validators (both Copilot and Claude Code) → Tasks 6, 7
- [x] Create command validation → Task 9 (end-to-end test via ValidateToolBlocks)
- [x] Tests → Tasks 1, 6, 7, 9, 10

**Placeholder scan:** No TBDs, TODOs, or vague steps. Every code block shows exact before/after changes. Every test step includes the expected outcome.

**Type consistency:** `Dictionary<string, string?>` used consistently across ToolConfig, ParseToolBlocks, ProgramTestHelper, and both validators. The null-check pattern (`value is not null`) is identical in CmdCopilot and CmdClaude.
