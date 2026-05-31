---
name: bare-flag-tool-parameters
description: Allow bare CLI flags (like --yolo) in TOOL block PARAMETER lines without a value
metadata:
  type: design
---

# Bare Flag Support in TOOL Block Parameters

## Motivation

The GitHub Copilot CLI has `--yolo` — a bare flag that takes no value (equivalent to `--allow-all`). The current Veron code always emits TOOL parameters as `--key value` pairs, making it impossible to express bare flags from modelfiles. This affects any future TOOL parameter that is a bare flag, not just Copilot.

## Syntax

Allow `PARAMETER key` without a value inside TOOL blocks:

```
TOOL copilot
  PARAMETER yolo
  PARAMETER effort high
END_TOOL
```

This produces: `copilot --model Qwopus --yolo --effort high ...`

Existing modelfiles with values are unaffected:

```
TOOL copilot
  PARAMETER effort high
END_TOOL
```

## Changes

### 1. ToolConfig model

Change `Dictionary<string, string>` → `Dictionary<string, string?>` to allow null values representing bare flags.

**File:** `Veron/Models/ToolConfig.cs`

### 2. ParseToolBlocks

When there's no space after the key (`spaceIdx < 0`), store `null` as the value instead of skipping the line.

**File:** `Veron/Parsing/ModelfileParser.cs`, lines 225-237

```csharp
// before: if (spaceIdx < 0) continue;
// after:
if (spaceIdx < 0)
{
    string key = rest.Trim();
    result[currentToolName].Parameters[key] = null; // bare flag
    continue;
}
```

### 3. CmdCopilot & CmdClaude

Check for null value before emitting args. If null, emit only `--key`.

**Files:** `Veron/Commands/CmdCopilot.cs` (lines 107-118), `Veron/Commands/CmdClaude.cs` (lines 130-142)

```csharp
// before:
copilotArgs.Add("--" + key);
copilotArgs.Add(value);

// after:
copilotArgs.Add("--" + key);
if (value is not null)
    copilotArgs.Add(value);
```

### 4. Validators

Both `CopilotValidator` and `ClaudeCodeValidator` need to handle null values. Bare flags have nothing to validate — return empty list early.

**Files:** `Veron/Validation/CopilotValidator.cs`, `Veron/Validation/ClaudeCodeValidator.cs`

```csharp
public static List<string> ValidateCopilotParameter(string key, string? value)
{
    if (value is null) return new(); // bare flag — nothing to validate
    // ... existing logic unchanged
}
```

### 5. Create command validation

`ModelfileValidator.ValidateToolBlocks` iterates `toolCfg.Parameters[key, value]` and calls the validator. Since validators now handle null, this works without changes.

**File:** `Veron/Validation/ModelfileValidator.cs` — no code change, but verify it works with null values in the dictionary.

### 6. Tests

Add tests for:
- Parsing `PARAMETER yolo` (no value) → null stored
- Copilot CLI args with bare flag → only `--yolo` emitted, no `null` string
- Claude Code CLI args with bare flag → same pattern
- Validator with null value → no errors

**File:** tests directory — add new test methods to existing test files.

## Scope

Focused and minimal — 4 source files changed, ~20 lines of code. No new abstractions, no hardcoded lists. Backward compatible — existing modelfiles with values continue working unchanged.
