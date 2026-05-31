# Help Restructure Design

## Problem

The current `veron --help` output is a wall of text. All command options, modelfile format docs, and examples are crammed into one message — users must scroll past lots of irrelevant info just to see what commands exist.

## Goal

Reduce top-level help to the essentials: command names with one-line descriptions. Move per-command detail behind `veron <command> --help`.

## Design

### Top-Level Help

The new top-level help is a slim listing:

```
USAGE
  veron <command> [options]

COMMANDS
  cat <name>          Show raw modelfile content
  ls, list            List all available modelfiles
  create <name> <path> Create a profile from a modelfile
  serve <name>        Start llama-server with the given model profile (foreground)
  claude <name>       Start llama-server then launch claude code
  copilot <name>      Start llama-server then launch copilot
  run <name>          Run llama-cli interactively with the given model profile
  ps                  List currently running servers
  stop [name]         Stop a specific server, or all if no name given
  remove, rm <name>   Remove a model profile
  help                Show this help message
  version             Show version information

GLOBAL OPTIONS
  --models-dir <dir>  Directory containing GGUF files

For more on a command: veron <command> --help
```

Changes from current output:
- Removed all per-command option lists (SERVE / CLAUDE OPTIONS, COPILOT OPTIONS, RUN OPTIONS)
- Removed modelfile format documentation
- Removed examples section
- Kept all 12 commands listed with one-line descriptions
- Kept global options
- Added footer directing users to `veron <command> --help`

### Subcommand Help

Each command supports `--help` to show its specific options. Dispatched via `CmdHelp.Run(command)`.

Commands with options (serve, claude, copilot, run, create, remove) show:

```
veron serve --help

USAGE
  veron serve <name> [options]

OPTIONS
  --alias <name>       Alias for the server (overwrites modelfile)
  --port <n>           Port (default: 5570)
  --context <n>        Context size (default: 128000)
  --jinja              Use Jinja template (default: on)
  --no-jinja           Disable Jinja template
  --flash-attention    Enable flash attention (default: on)
  --no-flash-attention Disable flash attention
  --repeat-penalty <f> Repeat penalty (default: 1.05)
  --n-gpu-layers <n>   GPU layers to offload
  --batch-size <n>     Batch size
  --wait <n>           Seconds to wait for server readiness (default: 30)
```

Commands with no options (ps, ls, list, help, version, stop, cat) show just usage and a description:

```
veron ps --help

USAGE
  veron ps

List currently running servers.
```

### Implementation Structure

1. **New `CmdHelp` class** — holds per-command help text as a data structure (command name → {usage, options, description})
2. **Dispatch detection in `Program.Main`** — before the command switch, check if `--help` or `-h` was passed in the options; if so, route to `CmdHelp.Run(command)` instead of the normal handler
3. **Split `PrintUsage()`** — current monolithic help method is replaced by:
   - Slim `PrintUsage()` for top-level (called by default, `--help`, `-h`, and unknown commands)
   - `CmdHelp.Run(string?)` for subcommand help (`null` = top-level)

### Out of Scope

- Modelfile format documentation lives in the README; not duplicated in CLI help
- Examples are not included in subcommand help (approach 1, not approach 3)
