---
name: server-tracking-and-remove
description: Server tracking infrastructure, remove command, enhanced stop/claude commands
metadata:
  type: design
---

# Server Tracking & Remove Command Design

## Overview

Veron currently has no way to track which model servers are running. This makes several things impossible: removing a model that's actively running, stopping a specific server, listing running servers, and knowing whether to restart llama-server when `claude` is invoked. This design adds per-server state tracking and uses it to enable the new `remove` command, improve `stop` and `claude`, and add a new `ps` command.

## Motivation

- **`rm` needs server awareness**: Before deleting a modelfile, we must know if llama-server is serving that model — otherwise the model stays in VRAM with no way to stop it (the modelfile is gone, so you can't target it again).
- **`stop` is blind**: The current `stop` command kills whatever PID is tracked, regardless of which model it's serving. We need `stop <name>`.
- **No visibility**: No way to see what's running, on what port, or when it started.
- **`claude` UX problem**: llama-server and Claude Code both dump output to the same terminal. Also, running `claude` twice starts two servers instead of reusing the existing one.

## Architecture

### Per-Server State Files

Each running server is tracked by a JSON file in `~/.veron/servers/<name>.json`. The file is created when the server starts and deleted when it stops.

**Schema:**

```json
{
  "model": "qwopus",
  "from": "Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf",
  "port": 5570,
  "context": 128000,
  "pid": 12345,
  "startedAt": "2026-05-30T15:30:00Z"
}
```

**Lifecycle rules:**

- `CmdServe` writes the state file after successfully starting llama-server
- Before starting, `CmdServe` reads the state file to check if already running — if PID is alive, skip and print a message
- If a state file exists but the PID is dead (process gone), treat as "not running" — clean up stale state silently
- On server stop, delete the state file

### Terminal Detection for Foreground Servers

When `claude --foreground` needs llama-server in a separate terminal window, detect the current terminal emulator. Priority: `$TERM_PROGRAM` → `$TERMINAL` → `/proc/$$/cmdline`. Supported terminals: gnome-terminal, konsole, xfce4-terminal, xterm. Fall back to background mode if detection fails.

## New Commands

### `ps` — List Running Servers

```bash
veron ps
```

Shows a table of all running servers from `~/.veron/servers/`. Skips stale entries (dead PIDs) and cleans them up.

```
NAME      MODEL FILE                         PORT    CONTEXT   PID      STARTED
qwopus    Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf  5570    128000    12345    15:30
minicpm   MiniCPM5-1B-Q4_K_M.gguf            5571    32000     12400    15:45
```

If no servers running: "No servers currently running."

Started column shows HH:MM for today, or full date + time if older than today.

### `remove` (short: `rm`) — Remove a Model Profile

```bash
veron rm qwopus
```

**Flow:**

1. Check if modelfile exists in `~/.veron/modelfiles/` — error and exit 1 if not
2. Prompt for confirmation: "Remove profile qwopus? [y/N]" — skipped if `-f` / `--force` is set
3. If a server is running for this model (state file exists + alive PID), stop it first and report it
4. Delete the modelfile from `~/.veron/modelfiles/`
5. Delete any state file in `~/.veron/servers/` (belt-and-suspenders)

```bash
veron rm qwopus -f    # skip confirmation prompt
```

The `-f` / `--force` flag follows the Unix convention (`rm -f`).

## Updated Commands

### `stop <name>?` — Stop Specific or All Servers

```bash
veron stop qwopus     # stop just this server by name
veron stop            # stop ALL running servers
```

**With a name:** Read that server's state file. If it exists and PID is alive, kill it, clean up, and report. If no state file or PID is dead, print "No server running for qwopus" (informational, not an error).

**Without a name:** Iterate all files in `~/.veron/servers/`, kill each alive PID, report what was stopped. Clean up any stale state files.

### `serve <name>` — Idempotent Server Start

Before starting llama-server, check if the target model's state file exists and its PID is alive. If so, skip and print:

```
Server for qwopus is already running (PID 12345, port 5570)
```

Exit 0 — this is not an error.

### `claude <name>` — Background Server + Foreground Claude Code

**Default (background mode):**

1. Check if server is already running for this model — if yes, skip starting llama-server
2. If not running, start llama-server as a background process and write state file
3. Wait for server readiness (existing `--wait` behavior) — if server fails to start or doesn't become ready within the wait timeout, abort and exit 1 without launching Claude Code
4. Launch Claude Code in foreground
5. When Claude exits: leave the server running

**Foreground mode (`-f` / `--foreground`):**

Same as above, but llama-server opens in a new terminal window detected from the current environment, showing server logs visually.

**Idempotent reuse:** Running `veron claude qwopus` twice reuses the existing server instead of starting a second one.

## Error Handling

- **Stale state cleanup**: State file exists but PID is dead → clean up silently, treat as "not running"
- **Modelfile not found on rm**: Exit 1 — "Profile qwopus not found in ~/.veron/modelfiles/"
- **No server for stop name**: Informational — "No server running for qwopus" (exit 0)
- **Force flag restriction**: `-f` / `--force` is only valid with the `remove` command. If passed to other commands, error: "Flag --force/-f is only valid with the remove command"

## Testing Approach

- Mock `PidManager` and process detection for `rm` / `stop` tests
- State file read/write tests for server tracking lifecycle (create, verify, stale cleanup, delete)
- Idempotency test: start server twice, verify only one starts
- `ps` test: verify table output with multiple servers and stale cleanup
- `rm` test: verify modelfile deletion, server stop, confirmation prompt behavior
