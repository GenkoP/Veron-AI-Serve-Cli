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
            case "copilot": CmdCopilot.Run(opts, modelsDir); break;
            case "stop":    CmdStop.Run(opts); break;
            case "ps":        CmdPs.Run(); break;
            case "remove":
            case "rm":        CmdRemove.Run(opts, modelsDir); break;
            default:         PrintUsage(); Environment.Exit(1); break;
        }
    }

    // ── Usage ──────────────────────────────────────────────────────────────

    static void PrintUsage() => CmdHelp.Run(null);

    // ── Version ─────────────────────────────────────────────────────────────

    static void PrintVersion()
    {
        Console.WriteLine("veron 1.0.0");
    }
}
