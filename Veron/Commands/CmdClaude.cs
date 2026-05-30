using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Veron;

static class CmdClaude
{
    const string DefaultClaudeBin = "claude";

    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = ModelfileParser.LoadConfig(opts, modelsDir, out string? modelfilePath);

        var cmd = LlamaServer.BuildLlamaCmd(cfg);

        Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " …");

        var psi = LlamaServer.ServerPsi(cmd);
        var serverProc = Process.Start(psi)
                        ?? throw new InvalidOperationException("Failed to start llama-server");

        PidManager.WritePid(serverProc.Id);
        string baseUrl = "http://localhost:" + cfg.Port;

        if (!LlamaServer.WaitForServer(baseUrl, cfg.Wait))
        {
            Console.Error.WriteLine("Error: server did not respond within " + cfg.Wait + "s");
            Environment.Exit(1);
        }

        Console.WriteLine("Server is ready at " + baseUrl);

        // Parse TOOL blocks from modelfile
        string toolName = "claude-code";
        Dictionary<string, ToolConfig>? toolConfigs = null;

        if (modelfilePath is not null)
        {
            try { toolConfigs = ModelfileParser.ParseToolBlocks(modelfilePath); }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(1);
            }
        }

        // Build claude code CLI arguments
        var claudeArgs = new List<string> { "code" };

        if (toolConfigs is not null && toolConfigs.TryGetValue(toolName, out var toolCfg))
        {
            foreach (var (key, value) in toolCfg.Parameters)
            {
                // Validate known parameters — unknown pass through silently
                var valErrors = ClaudeCodeValidator.ValidateClaudeCodeParameter(key, value);
                if (valErrors.Count > 0)
                {
                    Console.Error.WriteLine(valErrors[0]);
                    Environment.Exit(1);
                }

                claudeArgs.Add("--" + key);
                claudeArgs.Add(value);
            }

            Console.WriteLine("Using TOOL claude-code config from modelfile");
        }

        // Launch claude code
        string claudeBin = opts.GetValueOrDefault("claude-bin", DefaultClaudeBin);
        var claudePsi = new ProcessStartInfo(claudeBin, string.Join(" ", claudeArgs.Select(CliParser.EscapeArg)))
        {
            UseShellExecute = false,
        };
        claudePsi.EnvironmentVariables["ANTHROPIC_BASE_URL"]           = baseUrl;
        claudePsi.EnvironmentVariables["CLAUDE_CODE_ATTRIBUTION_HEADER"] = "0";

        Console.WriteLine();
        Console.WriteLine("Launching claude code …");
        Console.WriteLine("  ANTHROPIC_BASE_URL             = " + baseUrl);
        Console.WriteLine("  CLAUDE_CODE_ATTRIBUTION_HEADER = 0");

        // Print the tool args that were applied
        if (claudeArgs.Count > 1)
            Console.WriteLine("  TOOL claude-code args        = " + string.Join(" ", claudeArgs.Skip(1)));

        Console.WriteLine();

        try
        {
            var claudeProc = Process.Start(claudePsi)
                             ?? throw new FileNotFoundException(claudeBin);
            claudeProc.WaitForExit();
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is Win32Exception)
        {
            Console.Error.WriteLine("Error: couldn't launch '" + claudeBin + "' — make sure it's in $PATH.");
            Environment.Exit(1);
        }

        // Tear down the server
        Console.WriteLine("\nclaude code exited. Stopping llama-server …");
        if (!serverProc.HasExited)
            serverProc.Kill(true);
        PidManager.DeletePid();
        Console.WriteLine("Done.");
    }
}
