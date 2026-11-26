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
        ["serve"] = new("serve", "Run the Veron server (foreground); models load on demand", "veron serve [name] [options]", new[]
        {
            "[name]               Preload this profile (if the server already runs, only preload it)",
            "--host <addr>        Listen address (default: 127.0.0.1, or $VERON_HOST)",
            "--port <n>           Listen port (default: 11435, or $VERON_HOST)",
            "--keep-alive <dur>   Unload idle models after this time: 5m, 30s, 1h, 0, -1 = never (default: 5m)",
        }),
        ["claude"] = new("claude", "Launch claude code on a local model (via the Veron server)", "veron claude <name> [options]", new[]
        {
            "--claude-bin <path>  Claude Code binary (default: claude)",
        }),
        ["copilot"] = new("copilot", "Launch copilot on a local model (via the Veron server)", "veron copilot <name> [options]", new[]
        {
            "--prompt <text>      Execute a prompt in non-interactive mode (exits after completion)",
            "--copilot-bin <path> Copilot CLI binary (default: copilot)",
        }),
        ["run"] = new("run", "Chat with a model in the terminal (via the Veron server)", "veron run <name> [options]", new[]
        {
            "--prompt <text>      One-shot prompt, exit after response",
            "--temperature <f>    Sampling temperature (default: server/model default)",
            "--top-p <f>          Top-p sampling (default: server/model default)",
            "--no-color           Do not dim reasoning output",
        }),
        ["ps"] = new("ps", "List models loaded by the Veron server", "veron ps", Array.Empty<string>()),
        ["stats"] = new("stats", "Show live stats of the loaded model and recent requests", "veron stats [--watch]", new[]
        {
            "-w, --watch, watch   Redraw ~2x/s until Ctrl+C (one line per finished request when piped)",
        }),
        ["stop"] = new("stop", "Unload a model, or all models if no name given", "veron stop [name]", Array.Empty<string>()),
        ["shutdown"] = new("shutdown", "Stop the Veron server (unloads all models)", "veron shutdown", Array.Empty<string>()),
        ["remove"] = new("remove", "Remove a model profile (unloads it if loaded)", "veron remove <name>", new[]
        {
            "-f, --force       Skip confirmation prompt",
        }),
        ["rm"] = new("remove", "Remove a model profile (unloads it if loaded)", "veron remove <name>", new[]
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
        ("serve [name]", "Run the Veron server (foreground); models load on demand"),
        ("claude <name>", "Launch claude code on a local model"),
        ("copilot <name>", "Launch copilot on a local model"),
        ("run <name>", "Chat with a model in the terminal"),
        ("ps", "List loaded models"),
        ("stats [--watch]", "Show live stats and recent requests"),
        ("stop [name]", "Unload a model, or all models if no name given"),
        ("shutdown", "Stop the Veron server"),
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

ENVIRONMENT
  VERON_HOST           Veron server address (default: 127.0.0.1:11435)
  VERON_LLAMA_SERVER   Path to the llama-server binary

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
