# Veron — AI Model Serve CLI

Run local llama.cpp models the way Ollama does: one always-on server, models load automatically
when a request names them, swap on demand, and unload when idle. Launch Claude Code or Copilot CLI
against any local model with one command.

```
 claude / copilot / veron run / Open WebUI / any OpenAI or Anthropic client
                 │  http://127.0.0.1:11435   (OpenAI /v1/*, Anthropic /v1/messages, llama.cpp native)
                 ▼
        veron server ──► llama-server (one model at a time, internal port, started on demand)
```

## Requirements

- **.NET 10 SDK** with the ASP.NET Core runtime (included in the SDK) — [install from dot.net](https://dotnet.microsoft.com/download)
- **llama.cpp** — at minimum the `llama-server` binary. Clone and build as described in the [llama.cpp repository](https://github.com/ggerganov/llama.cpp):

```bash
git clone https://github.com/ggerganov/llama.cpp
cd llama.cpp && mkdir build && cd build
cmake .. -DBUILD_SHARED_LIBS=OFF
make -j$(nproc)
```

The default path to `llama-server` is set in `Veron/Process/LlamaServer.cs`; override it with the `VERON_LLAMA_SERVER` environment variable.

## Build

```bash
dotnet publish Veron/Veron.csproj -c Release -o bin/release
```

## Setup PATH

To use `veron` from any directory without specifying the full path, add the release output to your `$PATH`. Add one of the following lines to your shell profile (e.g., `~/.bashrc`, `~/.zshrc`):

```bash
# Option 1: symlink into ~/.local/bin (recommended if ~/.local/bin is in PATH)
ln -sf <project-dir>/bin/release/Veron ~/.local/bin/veron

# Option 2: add bin/release to PATH directly
export PATH="$PATH:<project-dir>/bin/release"
```

Then reload your shell or run `source ~/.bashrc`.

## Quick Start

```bash
# List all available model profiles
veron ls

# Chat with a model — the Veron server starts in the background and loads the model on demand
veron run qwopus
veron run qwopus --prompt "Explain quantum computing"

# Launch Claude Code / Copilot CLI on a local model (model is unloaded again when they exit)
veron claude qwopus
veron copilot qwopus --prompt "Fix the bug in main.js"

# Any OpenAI/Anthropic client works too — just name the profile as the model
curl http://127.0.0.1:11435/v1/chat/completions \
  -d '{"model":"qwopus","messages":[{"role":"user","content":"Hi"}]}'

# What is loaded, and until when
veron ps

# Unload a model / all models, stop the server
veron stop qwopus
veron stop
veron shutdown

# Run the server in the foreground (e.g. to watch its log), optionally preloading a model
veron serve
veron serve qwopus --keep-alive 30m
```

## How It Works

- **One server, one port.** `veron serve` (or any command that needs it) runs the Veron server on
  `127.0.0.1:11435` (Ollama uses 11434, so both can run side by side). Commands start it automatically
  in the background (`setsid`); its log is `~/.veron/logs/server.log`.
- **Models load on demand.** Each request's `"model"` field is matched to a profile (profile name first,
  then `alias`, case-insensitive). If that model is not loaded, Veron starts `llama-server` for it on a free
  internal port and forwards the request once it is healthy. Responses are streamed back unchanged (SSE).
  `GET /v1/models` lists all profiles.
- **One model at a time.** A request for a different model waits for in-flight requests to finish, stops
  the current `llama-server` and starts the new one.
- **Idle unload.** A model unloads after `keep_alive` without requests (default 5 minutes; per profile with
  `PARAMETER keep_alive`, globally with `veron serve --keep-alive`). `-1` = never, `0` = right after each request.
- **Tool sessions.** `veron claude` / `veron copilot` open a session that keeps the model loaded while the
  tool runs and unloads it when the tool exits — unless the model was already loaded before (e.g. by
  `veron run` or another client), or another session still uses it. Sessions of killed clients are
  cleaned up automatically.
- **Unknown model names** (e.g. Claude Code's background Haiku requests) go to the loaded model; with no
  model loaded they get a 404. A profile whose `.gguf` is missing gets a 404 explaining why.
- **Logs.** Each model's llama-server output goes to `~/.veron/logs/<profile>.log`; load failures return
  HTTP 503 with the last log lines.

## Commands

| Command | Short | Description |
|---------|-------|-------------|
| `cat <name>` | — | Show raw modelfile content |
| `list` | `ls` | List all available modelfile profiles |
| `create <name> <path>` | — | Copy and validate a modelfile as a profile into `~/.veron/modelfiles/` |
| `serve [name]` | — | Run the Veron server in the foreground; with a name, preload it (only preloads if the server already runs) |
| `claude <name>` | — | Load the model, launch Claude Code against it, unload when Claude exits |
| `copilot <name>` | — | Load the model, launch Copilot CLI against it, unload when Copilot exits |
| `run <name>` | — | Chat with the model in the terminal (`/bye` exits, `/clear` resets, Ctrl+C stops an answer) |
| `ps` | — | List loaded models and when they unload |
| `stop [name]` | — | Unload a model, or all models if no name given |
| `shutdown` | — | Stop the Veron server (unloads everything) |
| `remove <name>` | `rm` | Remove a model profile (unloads it if loaded) |
| `help` | `h` | Show help message |
| `version` | `v` | Show version information |

## Global Options

| Option | Description | Default |
|--------|-------------|---------|
| `--models-dir <dir>` | Directory containing GGUF files | `<your-models-path>` |

## Serve Options

| Option | Description | Default |
|--------|-------------|---------|
| `[name]` | Profile to preload | *(none)* |
| `--host <addr>` | Listen address | `127.0.0.1` |
| `--port <n>` | Listen port | `11435` |
| `--keep-alive <dur>` | Default idle time before unloading (`5m`, `30s`, `1h`, `0`, `-1` = never) | `5m` |

## Claude / Copilot Options

| Option | Description |
|--------|-------------|
| `<name>` | Profile name in `~/.veron/modelfiles/` (passed to the tool as `--model`) |
| `--prompt <text>` | *(copilot only)* run one prompt non-interactively |
| `--claude-bin <path>` / `--copilot-bin <path>` | Tool binary (default: `claude` / `copilot` from `$PATH`) |

## Create Options

| Option | Description |
|--------|-------------|
| `<name>` | Profile name (alphanumeric, hyphens, underscores only) |
| `<path>` | Path to the source modelfile on disk |

The `create` command validates the modelfile before copying it:
- Source file must exist
- Name must be valid (alphanumeric + `-` + `_`)
- FROM directive must be present and resolve to an actual `.gguf` on disk
- All parameters must use recognized keys with valid values
- Unknown parameter keys are rejected with error (catches typos)

```bash
# Create a profile from a modelfile
veron create my-model ~/my-modelfiles/qwopus.modelfile

# Overwrite an existing profile
veron create qwopus ~/updated-modelfile
```

## Remove Options

| Option | Description |
|--------|-------------|
| `<name>` | Profile name to remove |
| `-f` / `--force` | Skip confirmation prompt |

## Run Options

| Option | Description | Default |
|--------|-------------|---------|
| `<name>` | Profile name | *(required)* |
| `--prompt <text>` | One-shot prompt, exit after response | *(interactive)* |
| `--temperature <f>` | Sampling temperature | model/server default |
| `--top-p <f>` | Top-p sampling | model/server default |
| `--no-color` | Don't dim the model's reasoning output | |

`veron run` talks to the Veron server (`/v1/chat/completions`, streamed), so the model stays loaded between
runs and is shared with other clients.

## Modelfiles

All model profiles are defined as modelfiles in `~/.veron/modelfiles/`. **No extension required** — create files with any name you like.

When a request names a model, Veron looks for a file named `<name>` (with or without any extension) in the modelfiles directory and uses it as the profile.

### Modelfile Format

```
FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf

PARAMETER alias "Qwopus3.6-27b-MTP"
PARAMETER context 128000
PARAMETER jinja true
PARAMETER flash_attention true
PARAMETER repeat_penalty 1.05
PARAMETER n_gpu_layers 99
PARAMETER batch_size 512
PARAMETER wait 120
PARAMETER keep_alive 10m

# any other llama-server flags (lines accumulate, appended last so they win)
PARAMETER args "--split-mode tensor --spec-type draft-mtp --spec-draft-n-max 4 --spec-draft-p-min 0"

# ── Claude Code configuration ──────────────
TOOL claude-code
  PARAMETER permission-mode auto
  PARAMETER tools Bash,Edit,Read,Write
  PARAMETER append-system-prompt "You are working with a local model"
  PARAMETER effort high
END_TOOL

# ── GitHub Copilot CLI configuration ───
TOOL copilot
  PARAMETER effort high
  PARAMETER mode interactive
  PARAMETER allow-tool Bash,Edit,Read,Write
  PARAMETER log-level info
END_TOOL
```

- **`FROM`** — the model to use (filename or full path to `.gguf`)
- **`PARAMETER <key> <value>`** — llama-server settings:

| Key | Type | Description | Default |
|-----|------|-------------|---------|
| `alias` | string | Model alias (also accepted as the request's model name) | GGUF file name |
| `context` | integer | Context size (`-c`) | 128000 |
| `jinja` | boolean | Use the Jinja chat template | true |
| `flash_attention` | boolean | Flash attention | true |
| `repeat_penalty` | float | Repeat penalty | 1.05 |
| `n_gpu_layers` | integer | GPU layers (`-ngl`) | llama-server default |
| `batch_size` | integer | Batch size (`-b`) | llama-server default |
| `wait` | integer | Seconds to wait for the model to become ready | 120 |
| `keep_alive` | duration | Idle time before unloading: `5m`, `30s`, `1h`, `0`, `-1` (never) | server `--keep-alive` |
| `args` | string | Extra llama-server arguments, shell-quoted; several lines accumulate | — |
| `port` | integer | Ignored by the Veron server (it picks a free internal port) | — |

- **`TOOL <name>`** / **`END_TOOL`** — tool-specific CLI argument configuration (see below)

Lines starting with `#` are comments.

### TOOL Blocks: Configuring Agentic Tools

TOOL blocks let you define CLI arguments for agentic tools inside the modelfile. When you run `veron claude <name>`, Veron looks for a `TOOL claude-code` block and passes those parameters as flags to Claude Code.

A single modelfile can contain configuration for multiple tools:

```
FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf

PARAMETER context 128000

# Claude Code configuration
TOOL claude-code
  PARAMETER permission-mode auto
  PARAMETER tools Bash,Edit,Read,Write
  PARAMETER append-system-prompt "You are working with a local model"
  PARAMETER effort high
END_TOOL

# Future: other tool configurations (cursor, copilot, etc.)
TOOL cursor
  PARAMETER permission-mode plan
  PARAMETER max-turns 50
END_TOOL
```

The command name determines which TOOL block to use — `veron claude` uses `TOOL claude-code`. Future commands like `veron cursor` will use `TOOL cursor`. If no matching TOOL block exists, the tool launches with no extra arguments (backward compatible).

**Supported Claude Code parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `permission-mode` | string | Permission mode: `auto`, `plan`, `dontAsk`, `bypassPermissions`, `default`, `acceptEdits` |
| `tools` | string (comma-separated) | Restrict which built-in tools Claude can use |
| `disallowedTools` | string (comma-separated) | Deny specific tools or scoped rules |
| `allowedTools` | string (comma-separated) | Allow specific tools without prompting |
| `append-system-prompt` | string | Append text to the default system prompt |
| `effort` | string | Effort level: `low`, `medium`, `high`, `xhigh`, `max` |
| `max-budget-usd` | float | Maximum dollar amount to spend (print mode only) |
| `max-turns` | integer | Limit number of agentic turns (print mode only) |

Unknown parameters are passed through as-is, so future Claude Code flags work without needing Veron updates.

### Multiple Profiles per Model

Create different profiles for the same GGUF with different settings:

`~/.veron/modelfiles/qwopus` (full context):
```
FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf
PARAMETER alias "Qwopus3.6-27b-MTP"
PARAMETER context 128000
PARAMETER jinja true
PARAMETER flash_attention true
PARAMETER repeat_penalty 1.05
```

`~/.veron/modelfiles/qwopus-small` (smaller context):
```
FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf
PARAMETER alias "Qwopus3.6-27b-MTP"
PARAMETER context 32000
PARAMETER jinja true
PARAMETER flash_attention true
PARAMETER repeat_penalty 1.05
```

Then use them independently:

```bash
# Full context (128000)
veron run qwopus

# Smaller context (32000) — same model, different settings; the server swaps to it
veron run qwopus-small
```

### Listing Profiles

```bash
$ veron ls
NAME          FROM                                  SIZE
------------------------------------------------------------------------
     minicpm   MiniCPM5-1B-Q4_K_M.gguf              0.9 GB
      qwopus   Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf    16.8 GB
qwopus-small  Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf    missing

Total: 3 modelfile(s)
```

## Environment Variables

| Variable | Used by | Description |
|----------|---------|-------------|
| `VERON_HOST` | server + CLI | Server address, e.g. `127.0.0.1:11435`, `0.0.0.0:11435`, `:9000` |
| `VERON_LLAMA_SERVER` | server | Path to the `llama-server` binary |

### Set by `veron claude`

```bash
export ANTHROPIC_BASE_URL="http://127.0.0.1:11435"
export CLAUDE_CODE_ATTRIBUTION_HEADER=0
export ANTHROPIC_DEFAULT_HAIKU_MODEL="<profile>"   # background requests use the same local model
export ANTHROPIC_SMALL_FAST_MODEL="<profile>"
```

Then launches `claude code --model <profile>` plus the `TOOL claude-code` parameters.

### Set by `veron copilot`

```bash
export COPILOT_PROVIDER_TYPE="openai"
export COPILOT_PROVIDER_BASE_URL="http://127.0.0.1:11435/v1"
export COPILOT_MODEL="<profile>"
```

Then launches `copilot --model <profile>` plus the `TOOL copilot` parameters.

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

## Examples

```bash
# Create a profile from a modelfile
veron create my-model ~/modelfiles/my-modelfile

# Launch Claude Code with a profile (server + model start automatically)
veron claude qwopus

# Smaller-context variant of the same GGUF — a request for it swaps the model
veron claude qwopus-small

# Keep the server in the foreground with a 30 minute idle timeout
veron serve --keep-alive 30m

# Expose the server on the LAN on another port
VERON_HOST=0.0.0.0:8080 veron serve

# Remove a model profile without confirmation
veron rm qwopus -f
```
