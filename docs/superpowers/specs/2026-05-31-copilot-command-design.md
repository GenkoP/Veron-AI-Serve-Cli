# Copilot Command Design

**Date:** 2026-05-31
**Status:** Approved

## Overview

Add a new `veron copilot <name>` command that starts llama-server and launches GitHub Copilot CLI against it using BYOK (Bring Your Own Key) mode, mirroring the existing `veron claude` command pattern.

## Architecture

The `CmdCopilot` command follows the same flow as `CmdClaude`:

1. Parse modelfile + apply CLI overrides via `ModelfileParser.LoadConfig()`
2. Check if llama-server is already running for this model — reuse if so, otherwise start it and wait for readiness
3. Parse `TOOL copilot` block from modelfile to extract per-tool parameters
4. Set BYOK environment variables on the Copilot process
5. Build and launch `copilot --model <alias>` with any TOOL-block args appended
6. Wait for Copilot to exit, then stop llama-server (only if we started it)

### Environment Variables

These are set on every invocation:

| Variable | Value |
|----------|-------|
| `COPILOT_PROVIDER_TYPE` | `openai` |
| `COPILOT_PROVIDER_BASE_URL` | `http://localhost:<port>/v1` |
| `COPILOT_MODEL` | model alias from config |
| `COPILOT_OFFLINE` | `true` |

## Modelfile TOOL Block

A new `TOOL copilot` block in modelfiles, mirroring the existing `TOOL claude-code` pattern:

```
FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf
PARAMETER alias "Qwopus3.6-27b-MTP"
PARAMETER port 5570

TOOL copilot
  PARAMETER effort high
  PARAMETER mode interactive
  PARAMETER allow-tool Bash,Edit,Read,Write
  PARAMETER log-level info
END_TOOL
```

The existing `ModelfileParser.ParseToolBlocks()` handles parsing generically — no changes needed there. We look up `"copilot"` from the returned dictionary the same way `CmdClaude` looks up `"claude-code"`.

Parameters are mapped to CLI flags: keys are converted with hyphens (e.g., `allow-tool` → `--allow-tool`, `log-level` → `--log-level`). Boolean-like toggle flags (`bash-env`, `mouse`) accept `on`/`off` values.

Unknown TOOL parameters pass through without validation — Copilot CLI will catch errors at runtime.

## Command-Line Interface

```
veron copilot <name> [options]

Options:
  --prompt <text>     Execute a prompt in non-interactive mode (exits after completion)
```

The `--prompt` flag maps to `-p <text>` for non-interactive one-shot runs.

Everything else is configured via the modelfile and `TOOL copilot` block, or inherited from existing global options like `--models-dir`.

### Help Text

```
copilot <name>      Start llama-server then launch copilot (auto-stops server on exit)
```

## CopilotValidator

A new `CopilotValidator.cs` in the `Validation` directory validates known Copilot CLI parameter values at parse time.

### Enum Parameters

| Key | Valid Values |
|-----|-------------|
| `effort` / `reasoning-effort` | `none`, `low`, `medium`, `high`, `xhigh`, `max` |
| `mode` | `interactive`, `plan`, `autopilot` |
| `log-level` | `none`, `error`, `warning`, `info`, `debug`, `all`, `default` |
| `stream` | `on`, `off` |
| `output-format` | `text`, `json` |
| `bash-env` | `on`, `off` |
| `mouse` | `on`, `off` |

### Integer Parameters

| Key |
|-----|
| `max-autopilot-continues` |

Unknown parameters pass through without validation.

## Testing Strategy

- Unit tests for `CmdCopilot.Run()` verifying server reuse, env vars, and TOOL block arg passing — mirroring `ClaudeCommandTests.cs`
- Unit tests for `CopilotValidator.ValidateCopilotParameter()` covering valid/invalid enum values and integer params
- Integration: verify `copilot --prompt` works in non-interactive mode

## Files to Create/Modify

**New:**
- `Veron/Commands/CmdCopilot.cs` — command implementation
- `Veron/Validation/CopilotValidator.cs` — parameter validation

**Modified:**
- `Veron/Program.cs` — add `copilot` case to dispatch switch, update help text
- `tests/CopilotCommandTests.cs` — tests for the command
- `README.md` — document the new command
