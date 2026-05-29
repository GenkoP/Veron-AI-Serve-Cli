---
name: veron-create-command
description: Design for the `veron create` command — copy and validate user modelfiles into ~/.veron/modelfiles/
metadata:
  type: project
---

# Design: `veron create` Command

## Purpose

Allow users to register their own modelfiles into Veron's profile directory (`~/.veron/modelfiles/`) with full validation, so they appear in `veron ls` and can be served immediately.

## Command Signature

```bash
veron create <name> <path-to-modelfile>
```

| Argument | Description |
|----------|-------------|
| `<name>` | Profile name — becomes the filename in `~/.veron/modelfiles/` |
| `<path-to-modelfile>` | Path to the user's existing modelfile on disk |

The `--models-dir` global option applies here for resolving `FROM` paths.

## Validation Steps

Run in order, stop at the first failure:

1. **Source file exists** — the provided path points to a real, readable file
2. **Name sanitization** — `<name>` contains only alphanumeric characters, hyphens (`-`), and underscores (`_`); no path separators (`/`, `\`) or other special characters
3. **FROM directive present** — the modelfile contains at least one `FROM` line
4. **Model file exists** — the resolved model from `FROM` is an actual `.gguf` on disk (same resolution as `ParseModelfile`: absolute path → relative under `--models-dir` → append `.gguf`)
5. **Parameters valid** — all `PARAMETER` lines use recognized keys with parseable values

## Validation Error Messages

| Failure | Example Message |
|---------|----------------|
| Source file missing | `Error: source modelfile not found: /path/to/file` |
| Invalid name | `Error: invalid name "foo/bar": names must be alphanumeric, hyphens, or underscores` |
| No FROM directive | `Error: no FROM directive found in modelfile` |
| Model not found | `Error: model "Qwopus.gguf" not found in /models/dir` |
| Bad parameter value | `Error: invalid parameter "port": "abc" is not a valid integer` |
| Unknown parameter key | `Error: unknown parameter key: "foo" (did you mean one of: alias, port, context, jinja, flash_attention, repeat_penalty, n_gpu_layers, batch_size, wait?)` |

## Behavior

- If validation passes, **copy** the source file to `~/.veron/modelfiles/<name>`, overwriting any existing file silently (allows updates)
- Print a confirmation message on success
- Exit with non-zero status on any validation failure

## Success Output

```
$ veron create my-model /path/to/my-modelfile
Using modelfile: /path/to/my-modelfile
Creating profile "my-model" -> ~/.veron/modelfiles/my-model ✓
```

After creation, `veron ls` will list the new profile immediately.

## Code Organization

The existing `Program.cs` (~616 lines) contains all logic inline. The `create` command naturally shares validation with `serve`/`claude`, so:

- **Extract `ValidateModelfile(string sourcePath, string name, string modelsDir)`** — reads a modelfile, runs the existing `ParseModelfile` logic, catches and formats errors as strings. Returns a list of error strings (empty = valid). This is DRY — no duplication of parsing rules.
- **Add `CmdCreate(Dictionary<string,string> opts, string modelsDir)`** — the new command handler: extract `<name>` and `<path>` from positional args, sanitize name, validate, copy file, print success.
- **Wire into the switch dispatch** alongside the existing commands.

This keeps validation in one place (reused by `create`, potentially by `serve`/`claude` later) without refactoring unrelated code.

## Scope

Focused — one new command, no changes to existing behavior. The `ls` command already reads all files in `~/.veron/modelfiles/`, so no changes needed there.
