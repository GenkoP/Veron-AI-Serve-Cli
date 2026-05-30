using System;
using System.Collections.Generic;

namespace Veron;

public static class Program
{
    const string DefaultModelsDir   = "/home/genkop/Workspace/llama-cpp/models";

    // ── Entry point ────────────────────────────────────────────────────────
    static void Main(string[] rawArgs)
    {
        if (rawArgs.Length == 0 || rawArgs[0] == "--help" || rawArgs[0] == "-h")
        { PrintUsage(); return; }

        string command = rawArgs[0];
        var opts       = CliParser.ParseOpts(rawArgs.AsSpan(1));

        // Extract global options before dispatch
        string modelsDir = opts.GetValueOrDefault("models-dir") ?? CliParser.ExpandEnv(DefaultModelsDir);

        // Validate --force/-f is only used with remove command
        if (command != "remove" && command != "rm")
        {
            bool hasForce = false;
            foreach (var arg in rawArgs.Skip(1))
            {
                if (arg == "-f" || arg == "--force") { hasForce = true; break; }
            }

            if (hasForce)
            {
                Console.Error.WriteLine("Error: flag --force/-f is only valid with the remove command");
                Environment.Exit(1);
            }
        }

        switch (command)
        {
            case "cat":     CmdCat.Run(opts.GetValueOrDefault("model")
                    ?? throw new ArgumentNullException("model argument required for cat")); break;
            case "ls":
            case "list":    CmdList.Run(modelsDir); break;
            case "create":  CmdCreate.Run(opts, modelsDir); break;
            case "h":
            case "help":    PrintUsage(); break;
            case "v":
            case "version": PrintVersion(); break;
            case "serve":   CmdServe.Run(opts, modelsDir); break;
            case "claude":  CmdClaude.Run(opts, modelsDir); break;
            case "run":     CmdRun.Run(opts, modelsDir); break;
            case "stop":    CmdStop.Run(opts); break;
            case "ps":        CmdPs.Run(); break;
            case "remove":
            case "rm":        CmdRemove.Run(opts, modelsDir); break;
            default:         PrintUsage(); Environment.Exit(1); break;
        }
    }

    // ── Usage ──────────────────────────────────────────────────────────────

    static void PrintUsage()
    {
        Console.WriteLine(@"
Veron — manage llama.cpp models and launch claude code against them.

USAGE
  veron <command> [options]

COMMANDS
  cat <name>          Show raw modelfile content
  ls, list            List all available modelfiles
  create <name> <path> Create a profile from a modelfile (validates first)
  serve <name>        Start llama-server with the given model profile (foreground)
  claude <name>       Start llama-server then launch claude code (auto-stops server on exit)
  run <name>          Run llama-cli interactively with the given model profile
  ps                  List currently running servers
  stop [name]         Stop a specific server, or all if no name given
  remove, rm <name>   Remove a model profile (stops server if running)
    -f, --force       Skip confirmation prompt
  h, help             Show this help message
  v, version          Show version information

GLOBAL OPTIONS
  --models-dir <dir>   Directory containing GGUF files

SERVE / CLAUDE OPTIONS
  <name>               Modelfile name (without extension) in ~/.veron/modelfiles/
  --alias <name>       Alias for the server  (overwrites modelfile)
  --port <n>           Port  (default: 5570)
  --context <n>        Context size  (default: 128000)
  --jinja              Use Jinja template  (default: on)
  --no-jinja           Disable Jinja template
  --flash-attention    Enable flash attention  (default: on)
  --no-flash-attention Disable flash attention
  --repeat-penalty <f> Repeat penalty  (default: 1.05)
  --n-gpu-layers <n>   GPU layers to offload
  --batch-size <n>     Batch size
  --wait <n>           Seconds to wait for server readiness  (default: 30)
  --foreground        Start llama-server in a new terminal window (claude only)

RUN OPTIONS
  <name>               Modelfile name (without extension) in ~/.veron/modelfiles/
  --n-gpu-layers <n>   GPU layers to offload (default: -1 = full)
  --flash-attention    Enable flash attention (default: on)
  --no-flash-attention Disable flash attention
  --jinja              Use Jinja template (default: on)
  --no-jinja           Disable Jinja template
  --color              Enable colored output (default: on)
  --no-color           Disable colored output
  --temperature <f>    Temperature (default: 0.8)
  --top-p <f>          Top-p sampling (default: 0.9)
  --repeat-penalty <f> Repeat penalty (default: 1.1)
  --context <n>        Context size
  --prompt <text>      One-shot prompt, exit after response

MODELS
  Place modelfile files in ~/.veron/modelfiles/ — no extension required.

  Modelfile format (inspired by Ollama):

    FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf
    PARAMETER alias ""Qwopus3.6-27b-MTP""
    PARAMETER port 5570
    PARAMETER context 128000
    PARAMETER jinja true
    PARAMETER flash_attention true
    PARAMETER repeat_penalty 1.05
    PARAMETER n_gpu_layers 99
    PARAMETER batch_size 512
    PARAMETER wait 30

    TOOL claude-code
      PARAMETER permission-mode auto
      PARAMETER tools Bash,Edit,Read,Write
      PARAMETER append-system-prompt ""You are working with a local model""
      PARAMETER effort high
    END_TOOL

    TOOL parameters for claude-code:
      permission-mode     Permission mode (auto, plan, dontAsk, bypassPermissions, default, acceptEdits)
      tools               Comma-separated list of allowed tools
      disallowedTools     Comma-separated list of denied tools
      allowedTools        Comma-separated tools that don't need prompting
      append-system-prompt Append text to the system prompt
      effort              Effort level (low, medium, high, xhigh, max)
      max-budget-usd      Max dollar amount (print mode)
      max-turns           Agentic turn limit (print mode)

    Unknown TOOL parameters are passed through.

  You can create multiple profiles for the same GGUF model, e.g.:
    ~/.veron/modelfiles/my-model-small   (context 32000)
    ~/.veron/modelfiles/my-model-large   (context 128000)

  Command-line flags always override modelfile values.

EXAMPLES
  veron ls
  veron serve my-model-small
  veron claude my-model-large --port 5571
  veron run my-model
  veron run my-model --prompt ""Explain quantum computing""
  veron serve Qwopus3.6-27b-MTP

ENVIRONMENT (set automatically by 'claude')
  ANTHROPIC_BASE_URL          = http://localhost:<port>
  CLAUDE_CODE_ATTRIBUTION_HEADER = 0".TrimStart());
    }

    // ── Version ─────────────────────────────────────────────────────────────

    static void PrintVersion()
    {
        Console.WriteLine("veron 1.0.0");
    }
}
