---
name: run-command-design
description: Design for the `veron run` command that uses llama-cli for interactive model communication
metadata:
  date: 2026-05-30
  type: spec
---

# `veron run` Command Design

## Purpose

Add a `run` command that launches `llama-cli` (instead of `llama-server`) for direct interactive chat with a model. The modelfile provides the model path; all other parameters are CLI flags with sensible defaults.

## Architecture

Follows the existing pattern exactly — same structure as `serve` and `claude`:

```
Program.cs ──► CmdRun.Run()
                   │
                   ├─► ModelfileParser.LoadConfig()  ← gets model path, overlay CLI flags
                   │
                   ├─► LlamaCli.BuildLlamaCmd(cfg)    ← builds llama-cli arg list
                   │
                   └─► Process.Start(psi)              ← foreground, blocks until exit
```

### New Files

- `Veron/Process/LlamaCli.cs` — static helper for building `llama-cli` commands (mirrors `LlamaServer.cs`)
- `Veron/Commands/CmdRun.cs` — command handler

### Modified Files

- `Veron/Program.cs` — add `"run"` case in switch, update usage text
- `Veron/Models/ModelConfig.cs` — add new fields (`Color`, `Temperature`, `TopP`, `Prompt`)
- `Veron/Parsing/ModelfileParser.cs` — overlay CLI flags for new parameters
- `README.md` — document the new command

### No Changes To

- Existing commands (`serve`, `claude`, `stop`, `list`, `create`)
- `LlamaServer.cs`, `PidManager`, health check logic
- Modelfile parsing core — only overlay logic extends

## Key Differences from `serve`

| Aspect | `serve` (llama-server) | `run` (llama-cli) |
|---|---|---|
| Binary | `llama-server` | `llama-cli` |
| Interaction | HTTP server, background | Foreground, interactive terminal |
| Health check | `WaitForServer()` | None — ready immediately |
| PID tracking | Yes (`PidManager`) | No |
| Server flags | `-fa`, `--port` | `--flash-attn`, no port |
| Default GPU offload | None (optional) | Full (`-ngl -1`) |

## CLI Flags

| Flag | Default | Description |
|---|---|---|
| `<name>` | — | Modelfile name in `~/.veron/modelfiles/` |
| `--n-gpu-layers <n>` | `-1` | GPU layers (`-1` = full offload) |
| `--flash-attention` / `--no-flash-attention` | on | Flash attention |
| `--jinja` / `--no-jinja` | on | Jinja template |
| `--color` / `--no-color` | on | Colored output |
| `--temperature <f>` | `0.8` | Temperature |
| `--top-p <f>` | `0.9` | Top-p sampling |
| `--repeat-penalty <f>` | `1.1` | Repeat penalty |
| `--context <n>` | from modelfile | Context size |
| `--prompt <text>` | — | One-shot prompt, exit after response |

## ModelConfig Additions

Four new fields on `ModelConfig`:

- **`Color`** (`bool`, default `true`) — controls `--color` / `--no-color`
- **`Temperature`** (`float?`, default `0.8f`) — controls `--temperature`
- **`TopP`** (`float?`, default `0.9f`) — controls `--top-p`
- **`Prompt`** (`string?`, default `null`) — one-shot prompt text

These defaults are applied in `LlamaCli.BuildLlamaCmd()` — not in the modelfile parser — so they only affect `run`, not `serve` or `claude`.

## LlamaCli Helper Class

```csharp
static class LlamaCli
{
    const string DefaultBinary = "/home/genkop/Workspace/llama-cpp/llama.cpp/build/bin/llama-cli";

    public static List<string> BuildLlamaCmd(ModelConfig cfg)
    {
        var cmd = new List<string>
        {
            DefaultBinary,
            "-m", cfg.ModelPath,
            "--alias", cfg.Alias,
            "-ngl", cfg.NGpuLayers ?? -1,
            "-c", cfg.Context.ToString(),
            "--repeat-penalty", cfg.RepeatPenalty.ToString("0.00"),
        };

        if (cfg.Fa) cmd.Add("--flash-attn");
        if (cfg.Jinja) cmd.Add("--jinja");
        if (cfg.Color ?? true) cmd.Add("--color");

        if (cfg.Temperature.HasValue)
            cmd.AddRange(new[] { "--temperature", cfg.Temperature.Value.ToString("0.00") });

        if (cfg.TopP.HasValue)
            cmd.AddRange(new[] { "--top-p", cfg.TopP.Value.ToString("0.00") });

        if (cfg.Prompt is not null)
        {
            cmd.AddRange(new[] { "--prompt", cfg.Prompt });
            cmd.Add("--single-turn"); // exit after response, don't enter interactive mode
        }

        return cmd;
    }

    public static ProcessStartInfo CliPsi(List<string> cmd)
    {
        return new ProcessStartInfo(cmd[0], string.Join(" ", cmd.Skip(1).Select(CliParser.EscapeArg)))
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
    }
}
```

## CmdRun Command Handler

```csharp
public static void Run(Dictionary<string, string> opts, string modelsDir)
{
    var cfg = ModelfileParser.LoadConfig(opts, modelsDir, out _);

    var cmd = LlamaCli.BuildLlamaCmd(cfg);

    Console.WriteLine("Running llama-cli for " + cfg.Alias + " …");
    Console.WriteLine("  Command: " + string.Join(" ", cmd.Select(CliParser.EscapeArg)));

    var psi = LlamaCli.CliPsi(cmd);
    var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start llama-cli");

    try { proc.WaitForExit(); }
    catch (OperationCanceledException) { } // Ctrl+C
    finally
    {
        if (!proc.HasExited)
        {
            Console.WriteLine("\nShutting down …");
            proc.Kill(true);
            Console.WriteLine("Stopped.");
        }
    }
}
```

## One-Shot Prompt Mode

When `--prompt <text>` is provided, the prompt is passed to `llama-cli` via its `--prompt` flag. After llama-cli processes the prompt and exits, Veron exits too — no interactive loop.

This works because `llama-cli --prompt "..." --single-turn` will generate a response and exit. Without `--single-turn`, `llama-cli` would enter interactive mode after the first response.

## Testing Approach

- Test that `veron run <name>` starts `llama-cli` with expected args (unit test similar to existing `ToolParsingTests`)
- Test that `--prompt` flag is passed through correctly
- Test Ctrl+C gracefully kills the process via `OperationCanceledException` handler

## Scope Boundaries

This spec covers **only** the `run` command. It does NOT cover:

- Adding `llama-cli` parameters to modelfile PARAMETER directives
- TOOL blocks for `llama-cli`
- Background mode for `llama-cli`
- Refactoring shared abstractions between `LlamaServer` and `LlamaCli`
