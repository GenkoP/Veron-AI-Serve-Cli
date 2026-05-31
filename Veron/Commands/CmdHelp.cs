using System;
using System.Collections.Generic;
using System.Linq;

namespace Veron;

static class CmdHelp
{
    // ── Help data per command ────────────────────────────────────────────────

    static readonly Dictionary<string, CommandHelp> Commands = new()
    {
        ["cat"] = new("cat", "Show raw modelfile content", "veron cat <name>", Array.Empty<string>()),
        ["ls"] = new("ls", "List all available modelfiles", "veron ls", Array.Empty<string>()),
        ["list"] = new("ls", "List all available modelfiles", "veron ls", Array.Empty<string>()),
        ["create"] = new("create", "Create a profile from a modelfile", "veron create <name> <path>", Array.Empty<string>()),
        ["serve"] = new("serve", "Start llama-server with the given model profile (foreground)", "veron serve <name> [options]", new[]
        {
            "--alias <name>       Alias for the server (overwrites modelfile)",
            "--port <n>           Port (default: 5570)",
            "--context <n>        Context size (default: 128000)",
            "--jinja              Use Jinja template (default: on)",
            "--no-jinja           Disable Jinja template",
            "--flash-attention    Enable flash attention (default: on)",
            "--no-flash-attention Disable flash attention",
            "--repeat-penalty <f> Repeat penalty (default: 1.05)",
            "--n-gpu-layers <n>   GPU layers to offload",
            "--batch-size <n>     Batch size",
            "--wait <n>           Seconds to wait for server readiness (default: 30)",
        }),
        ["claude"] = new("claude", "Start llama-server then launch claude code", "veron claude <name> [options]", new[]
        {
            "--alias <name>       Alias for the server (overwrites modelfile)",
            "--port <n>           Port (default: 5570)",
            "--context <n>        Context size (default: 128000)",
            "--jinja              Use Jinja template (default: on)",
            "--no-jinja           Disable Jinja template",
            "--flash-attention    Enable flash attention (default: on)",
            "--no-flash-attention Disable flash attention",
            "--repeat-penalty <f> Repeat penalty (default: 1.05)",
            "--n-gpu-layers <n>   GPU layers to offload",
            "--batch-size <n>     Batch size",
            "--wait <n>           Seconds to wait for server readiness (default: 30)",
            "--foreground         Start llama-server in a new terminal window",
        }),
        ["copilot"] = new("copilot", "Start llama-server then launch copilot", "veron copilot <name> [options]", new[]
        {
            "--alias <name>       Alias for the server (overwrites modelfile)",
            "--port <n>           Port (default: 5570)",
            "--context <n>        Context size (default: 128000)",
            "--jinja              Use Jinja template (default: on)",
            "--no-jinja           Disable Jinja template",
            "--flash-attention    Enable flash attention (default: on)",
            "--no-flash-attention Disable flash attention",
            "--repeat-penalty <f> Repeat penalty (default: 1.05)",
            "--n-gpu-layers <n>   GPU layers to offload",
            "--batch-size <n>     Batch size",
            "--wait <n>           Seconds to wait for server readiness (default: 30)",
            "--prompt <text>      Execute a prompt in non-interactive mode (exits after completion)",
        }),
        ["run"] = new("run", "Run llama-cli interactively with the given model profile", "veron run <name> [options]", new[]
        {
            "--n-gpu-layers <n>   GPU layers to offload (default: -1 = full)",
            "--flash-attention    Enable flash attention (default: on)",
            "--no-flash-attention Disable flash attention",
            "--jinja              Use Jinja template (default: on)",
            "--no-jinja           Disable Jinja template",
            "--color              Enable colored output (default: on)",
            "--no-color           Disable colored output",
            "--temperature <f>    Temperature (default: 0.8)",
            "--top-p <f>          Top-p sampling (default: 0.9)",
            "--repeat-penalty <f> Repeat penalty (default: 1.1)",
            "--context <n>        Context size",
            "--prompt <text>      One-shot prompt, exit after response",
        }),
        ["ps"] = new("ps", "List currently running servers", "veron ps", Array.Empty<string>()),
        ["stop"] = new("stop", "Stop a specific server, or all if no name given", "veron stop [name]", Array.Empty<string>()),
        ["remove"] = new("remove", "Remove a model profile (stops server if running)", "veron remove <name>", new[]
        {
            "-f, --force       Skip confirmation prompt",
        }),
        ["rm"] = new("remove", "Remove a model profile (stops server if running)", "veron remove <name>", new[]
        {
            "-f, --force       Skip confirmation prompt",
        }),
        ["help"] = new("help", "Show this help message", "veron help", Array.Empty<string>()),
        ["version"] = new("version", "Show version information", "veron version", Array.Empty<string>()),
    };

    // Canonical command list for top-level help display (with aliases shown)
    static readonly (string Display, string Description)[] TopLevelCommands =
    [
        ("cat <name>", "Show raw modelfile content"),
        ("ls, list", "List all available modelfiles"),
        ("create <name> <path>", "Create a profile from a modelfile"),
        ("serve <name>", "Start llama-server with the given model profile (foreground)"),
        ("claude <name>", "Start llama-server then launch claude code"),
        ("copilot <name>", "Start llama-server then launch copilot"),
        ("run <name>", "Run llama-cli interactively with the given model profile"),
        ("ps", "List currently running servers"),
        ("stop [name]", "Stop a specific server, or all if no name given"),
        ("remove, rm <name>", "Remove a model profile"),
        ("help", "Show this help message"),
        ("version", "Show version information"),
    ];

    // ── Public API ───────────────────────────────────────────────────────────

    public static CommandHelp? Get(string commandName)
    {
        if (Commands.TryGetValue(commandName, out var entry))
            return entry;
        return null;
    }

    public static string[] TopLevelCommandNames() => Commands.Keys.ToArray();

    /// <summary>Print per-command help for the given command, or top-level help if null.</summary>
    public static void Run(string? commandName)
    {
        if (commandName is null)
        { PrintTopLevel(); return; }

        var entry = Get(commandName);
        if (entry is null)
        {
            Console.Error.WriteLine($"Unknown command: {commandName}");
            PrintTopLevel();
            Environment.Exit(1);
            return;
        }

        PrintCommand(entry.Value);
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    static void PrintCommand(CommandHelp entry)
    {
        Console.WriteLine("USAGE");
        Console.WriteLine($"  {entry.Usage}");

        if (entry.Options.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("OPTIONS");
            foreach (var opt in entry.Options)
                Console.WriteLine($"  {opt}");
        }
    }

    static void PrintTopLevel()
    {
        Console.WriteLine(@"
USAGE
  veron <command> [options]

COMMANDS".TrimStart());

        // Align descriptions at column 30
        int maxDisplay = TopLevelCommands.Max(c => c.Display.Length);
        foreach (var (display, desc) in TopLevelCommands)
        {
            Console.WriteLine($"  {display.PadRight(maxDisplay + 2)}{desc}");
        }

        Console.WriteLine(@"
GLOBAL OPTIONS
  --models-dir <dir>   Directory containing GGUF files

For more on a command: veron <command> --help".TrimStart());
    }
}

/// <summary>Help data for a single command.</summary>
readonly struct CommandHelp(string displayName, string description, string usage, string[] options)
{
    public string DisplayName => displayName;
    public string Description => description;
    public string Usage => usage;
    public string[] Options => options;
}
