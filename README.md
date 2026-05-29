# Veron — AI Model Serve CLI

Manage llama.cpp models and launch Claude Code against them, similar to how Ollama works.

## Quick Start

```bash
# List all available model profiles
veron ls

# Serve a model profile (foreground)
veron serve minicpm

# Serve with CLI override on port
veron serve minicpm --port 8080

# Launch Claude Code with the server (auto-stops when done)
veron claude qwopus

# Stop a previously started server
veron stop

# Show help / version
veron h
veron v
```

## Commands

| Command | Short | Description |
|---------|-------|-------------|
| `list` | `ls` | List all available modelfile profiles |
| `serve <name>` | — | Start llama-server with the given profile (runs in foreground) |
| `claude <name>` | — | Start llama-server, set env vars, then launch `claude code`. Auto-stops when claude exits |
| `stop` | — | Stop a previously started llama-server |
| `help` | `h` | Show help message |
| `version` | `v` | Show version information |

## Global Options

| Option | Description | Default |
|--------|-------------|---------|
| `--models-dir <dir>` | Directory containing GGUF files | `/home/genkop/Workspace/llama-cpp/models` |

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
```

- **`FROM`** — the model to use (filename or full path to `.gguf`)
- **`PARAMETER <key> <value>`** — any of the serve options listed above

Lines starting with `#` are comments.

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
NAME          FROM
---------------------------
     minicpm   MiniCPM5-1B-Q4_K_M.gguf
      qwopus   Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf
qwopus-small  Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf

Total: 3 modelfile(s)
```

## Environment Variables (set by `claude`)

When you run `veron claude`, it automatically sets:

```bash
export ANTHROPIC_BASE_URL="http://localhost:<port>"
export CLAUDE_CODE_ATTRIBUTION_HEADER=0
```

Then launches `claude code` with those env vars.

## Examples

```bash
# List model profiles
veron ls

# Serve a profile from modelfile
veron serve minicpm

# Serve with CLI override
veron serve minicpm --port 8080

# Launch Claude Code with a profile
veron claude qwopus

# Launch Claude Code with smaller context profile
veron claude qwopus-small --port 5571

# Serve with GPU offloading and no Jinja
veron serve qwopus --n-gpu-layers 99 --no-jinja

# Show help or version
veron h
veron v
```

## Build

```bash
dotnet publish -c Release -o bin/release
```

Requires .NET 10 SDK and `llama-server` in the configured path.
