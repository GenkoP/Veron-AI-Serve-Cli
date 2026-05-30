# Veron — AI Model Serve CLI

Manage llama.cpp models and launch Claude Code against them, similar to how Ollama works.

## Requirements

- **.NET 8+ SDK** — [install from dot.net](https://dotnet.microsoft.com/download/dotnet/8.0)
- **llama.cpp** — at minimum the `llama-server` binary. Clone and build as described in the [llama.cpp repository](https://github.com/ggerganov/llama.cpp):

```bash
git clone https://github.com/ggerganov/llama.cpp
cd llama.cpp && mkdir build && cd build
cmake .. -DBUILD_SHARED_LIBS=OFF
make -j$(nproc)
```

The path to `llama-server` is configured in `Veron/Process/LlamaServer.cs` — update it if your binary lives elsewhere.

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

# Create a profile from a modelfile (with validation)
veron create my-model /path/to/modelfile

# Serve a model profile (foreground)
veron serve minicpm

# Serve with CLI override on port
veron serve minicpm --port 8080

# Launch Claude Code with the server (auto-stops when done)
veron claude qwopus

# Interactive chat with llama-cli
veron run qwopus

# One-shot prompt
veron run qwopus --prompt "Explain quantum computing"

# Stop a previously started server
veron stop

# Remove a model profile (stops server if running)
veron rm qwopus -f

# Show help / version
veron h
veron v
```

## Commands

| Command | Short | Description |
|---------|-------|-------------|
| `list` | `ls` | List all available modelfile profiles |
| `create <name> <path>` | — | Copy and validate a modelfile as a profile into `~/.veron/modelfiles/` |
| `serve <name>` | — | Start llama-server with the given profile (runs in foreground) |
| `claude <name>` | — | Start llama-server, set env vars, then launch `claude code`. Auto-stops when claude exits |
| `run <name>` | — | Run llama-cli interactively with the given model profile |
| `stop` | — | Stop a specific server by name, or all if no name given |
| `ps` | — | List currently running servers |
| `remove <name>` | `rm` | Remove a model profile (stops server if running) |
| `help` | `h` | Show help message |
| `version` | `v` | Show version information |

## Global Options

| Option | Description | Default |
|--------|-------------|---------|
| `--models-dir <dir>` | Directory containing GGUF files | `<your-models-path>` |

## Serve / Claude Options

| Option | Description | Default |
|--------|-------------|---------|
| `<name>` | Modelfile name (without extension) in `~/.veron/modelfiles/` | *(required)* |
| `--alias <name>` | Alias for the server | from modelfile |
| `--port <n>` | Port | from modelfile |
| `--context <n>` | Context size | from modelfile |
| `--jinja` / `--no-jinja` | Use Jinja template | on |
| `--flash-attention` / `--no-flash-attention` | Flash attention | on |
| `--repeat-penalty <f>` | Repeat penalty | from modelfile |
| `--n-gpu-layers <n>` | GPU layers to offload | *(auto)* |
| `--batch-size <n>` | Batch size | *(auto)* |
| `--wait <n>` | Seconds to wait for server readiness | 30 |

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
| `<name>` | Modelfile name (without extension) in `~/.veron/modelfiles/` | *(required)* |
| `--n-gpu-layers <n>` | GPU layers to offload | `-1` (full) |
| `--flash-attention` / `--no-flash-attention` | Flash attention | on |
| `--jinja` / `--no-jinja` | Use Jinja template | on |
| `--color` / `--no-color` | Colored output | on |
| `--temperature <f>` | Temperature | 0.8 |
| `--top-p <f>` | Top-p sampling | 0.9 |
| `--repeat-penalty <f>` | Repeat penalty | 1.1 |
| `--context <n>` | Context size | from modelfile |
| `--prompt <text>` | One-shot prompt, exit after response | *(interactive)* |

The `run` command uses `llama-cli` (not `llama-server`) for direct interactive chat with a model. The modelfile provides the model path; all other parameters are CLI flags with sensible defaults.

## Modelfiles

All model profiles are defined as modelfiles in `~/.veron/modelfiles/`. **No extension required** — create files with any name you like.

When you run `veron serve <name>`, Veron looks for a file named `<name>` (with or without any extension) in the modelfiles directory and uses it as the profile.

### Modelfile Format

```
FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf

PARAMETER alias "Qwopus3.6-27b-MTP"
PARAMETER port 5570
PARAMETER context 128000
PARAMETER jinja true
PARAMETER flash_attention true
PARAMETER repeat_penalty 1.05
PARAMETER n_gpu_layers 99
PARAMETER batch_size 512
PARAMETER wait 30

# ── Claude Code configuration ──────────────
TOOL claude-code
  PARAMETER permission-mode auto
  PARAMETER tools Bash,Edit,Read,Write
  PARAMETER append-system-prompt "You are working with a local model"
  PARAMETER effort high
END_TOOL
```

- **`FROM`** — the model to use (filename or full path to `.gguf`)
- **`PARAMETER <key> <value>`** — any of the serve options listed above
- **`TOOL <name>`** / **`END_TOOL`** — tool-specific CLI argument configuration (see below)

Lines starting with `#` are comments.

### TOOL Blocks: Configuring Agentic Tools

TOOL blocks let you define CLI arguments for agentic tools inside the modelfile. When you run `veron claude <name>`, Veron looks for a `TOOL claude-code` block and passes those parameters as flags to Claude Code.

A single modelfile can contain configuration for multiple tools:

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
PARAMETER port 5570
PARAMETER context 128000
PARAMETER jinja true
PARAMETER flash_attention true
PARAMETER repeat_penalty 1.05
```

`~/.veron/modelfiles/qwopus-small` (smaller context):
```
FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf
PARAMETER alias "Qwopus3.6-27b-MTP"
PARAMETER port 5570
PARAMETER context 32000
PARAMETER jinja true
PARAMETER flash_attention true
PARAMETER repeat_penalty 1.05
```

Then use them independently:

```bash
# Full context (128000)
veron serve qwopus

# Smaller context (32000) — same model, different settings
veron serve qwopus-small

# Override port from CLI
veron serve qwopus --port 8080
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

## Run Command

The `run` command uses `llama-cli` (not `llama-server`) for direct interactive chat with a model. It starts with sensible defaults: full GPU offload (`-ngl -1`), flash attention, colored output, and Jinja template.

```bash
# Interactive chat
veron run qwopus

# With temperature override
veron run qwopus --temperature 0.3

# One-shot prompt — runs a single prompt and exits
veron run qwopus --prompt "Explain quantum computing"
```

## Environment Variables (set by `claude`)

When you run `veron claude`, it automatically sets:

```bash
export ANTHROPIC_BASE_URL="http://localhost:<port>"
export CLAUDE_CODE_ATTRIBUTION_HEADER=0
```

Then launches `claude code` with those env vars. TOOL block parameters are passed as CLI flags to the Claude Code command.

## Examples

```bash
# List model profiles
veron ls

# Create a profile from a modelfile
veron create my-model ~/modelfiles/my-modelfile

# Serve a profile from modelfile
veron serve minicpm

# Serve with CLI override
veron serve minicpm --port 8080

# Launch Claude Code with a profile
veron claude qwopus

# Launch Claude Code with smaller context profile
veron claude qwopus-small --port 5571

# Interactive chat with llama-cli
veron run qwopus

# One-shot prompt mode
veron run qwopus --prompt "Explain quantum computing"

# Remove a model profile (with confirmation)
veron rm qwopus

# Remove without confirmation
veron rm qwopus -f

# Serve with GPU offloading and no Jinja
veron serve qwopus --n-gpu-layers 99 --no-jinja

# Show help or version
veron h
veron v
```

