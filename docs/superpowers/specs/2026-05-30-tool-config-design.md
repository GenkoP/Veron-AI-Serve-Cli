# Design: Tool Configuration in Modelfiles

## Purpose

Allow modelfiles to configure CLI arguments for agentic tools (Claude Code now, Cursor, Copilot, etc. later). A single modelfile can contain configuration for multiple tools. When the user runs `veron claude <name>`, the TOOL `claude-code` section (if present) provides the CLI flags passed to Claude Code.

## Motivation

Users want to define everything in one place — the modelfile — rather than remembering per-invocation CLI arguments. This is especially useful for `permission-mode` (skip prompts on local models), `tools` (restrict toolset), and `append-system-prompt` (inject context about using a local model).

## Design

### Modelfile Extension: TOOL Blocks

A new section type, `TOOL <name> ... END_TOOL`, defines tool-specific parameter configuration.

```
FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf

PARAMETER port 5570
PARAMETER context 128000

# Claude Code configuration
TOOL claude-code
  PARAMETER permission-mode auto
  PARAMETER tools Bash,Edit,Read,Write
  PARAMETER append-system-prompt "You are working with a local model"
  PARAMETER effort high
END_TOOL

# Future: other tool configurations
TOOL cursor
  PARAMETER permission-mode plan
  PARAMETER max-turns 50
END_TOOL
```

**Rules:**

- `TOOL <name>` starts a block; `END_TOOL` ends it.
- Parameters inside use the same `PARAMETER key value` syntax as global parameters.
- A modelfile can have zero, one, or multiple TOOL blocks.
- Zero TOOL blocks: backward compatible — `veron claude` works with no extra args (as today).
- Multiple TOOL blocks let one modelfile configure different tools for different commands.
- Unknown TOOL block names are silently ignored (forward-compatible).

### Command-to-Tool Mapping

The command name determines which TOOL block to use:

| Veron Command | TOOL Name |
|---|---|
| `veron claude <name>` | `claude-code` |
| `veron cursor <name>` (future) | `cursor` |

No `--tool` flag needed — the command implies the tool.

### Supported Claude Code Parameters (Essential Set)

| Parameter | Type | Valid Values | Description |
|---|---|---|---|
| `permission-mode` | string | `auto`, `plan`, `dontAsk`, `bypassPermissions`, `default`, `acceptEdits` | Permission mode for the session |
| `tools` | string (comma-separated) | Tool names or `""` | Restrict which built-in tools Claude can use |
| `disallowedTools` | string (comma-separated) | Tool names or scoped rules | Deny specific tools |
| `allowedTools` | string (comma-separated) | Tool names or scoped rules | Allow tools without prompting |
| `append-system-prompt` | string | any text | Append to default system prompt |
| `effort` | string | `low`, `medium`, `high`, `xhigh`, `max` | Model effort level |
| `max-budget-usd` | float | any positive number | Max dollar amount (print mode only) |
| `max-turns` | integer | any positive integer | Agentic turn limit (print mode only) |

**Unknown passthrough:** Parameters not in the known set above are passed through as `--<key> <value>` anyway. This ensures future Claude Code flags work without Veron needing updates.

### Parameter Validation

- Known parameters: validated against expected types and valid values.
  - `permission-mode` must be one of the known modes.
  - `effort` must be one of `low|medium|high|xhigh|max`.
  - `max-budget-usd` must parse as a float.
  - `max-turns` must parse as an integer.
- Unknown parameters: passed through without validation.

### Command Flow: `veron claude <name>`

1. Parse modelfile → get `ModelConfig` (FROM + global PARAMETERs) — same as today.
2. Additionally parse TOOL blocks into a dictionary keyed by tool name.
3. Build llama-server command, start it, wait for readiness — same as today.
4. Build Claude Code CLI arguments:
   - Base args: `code`
   - If `TOOL claude-code` exists, add each parameter as `--<key> <value>`
5. Set env vars (`ANTHROPIC_BASE_URL`, `CLAUDE_CODE_ATTRIBUTION_HEADER`) — same as today.
6. Launch Claude Code with those args.
7. When Claude Code exits, stop llama-server — same as today.

**Example:**

```bash
veron claude my-model
# → starts llama-server on port 5570
# → launches: claude code \
#     --permission-mode auto \
#     --tools Bash,Edit,Read,Write \
#     --append-system-prompt "You are working with a local model" \
#     --effort high
```

### Error Handling

- `END_TOOL` without matching `TOOL` → error, exit.
- Nested TOOL blocks (TOOL inside TOOL) → error, exit.
- Invalid parameter value for a known parameter → error message, exit.
- Unknown tool names silently ignored — no error.

### Integration with `veron create`

The `ValidateModelfile` helper should also validate TOOL block parameters when creating a profile. Same validation rules as above. Unknown parameters in TOOL blocks are allowed (passthrough) — they don't cause validation failure. Only known parameters with invalid values are rejected. This matches the run-time behavior: Veron doesn't need to know every Claude Code flag to be useful.

## Data Structures

```csharp
// New: holds per-tool parameter configuration
class ToolConfig
{
    public string Name           { get; set; } = "";  // "claude-code", "cursor", etc.
    public Dictionary<string, string> Parameters { get; set; } = new();
}

// Existing ModelConfig — no changes needed (tool config is separate)
```

## Backward Compatibility

- Modelfiles without TOOL blocks work exactly as before.
- `veron claude` without a matching TOOL block launches with no extra args (as today).
- Existing parameters are unchanged.
- No breaking changes to the CLI interface.
