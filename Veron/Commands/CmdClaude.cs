using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Veron;

static class CmdClaude
{
    const string DefaultClaudeBin = "claude";

    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = ModelfileParser.LoadConfig(opts, modelsDir, out string? modelfilePath);

        // The model profile name the user typed
        string modelName = opts.GetValueOrDefault("model") ?? throw new ArgumentNullException("model argument required");

        bool foreground = CliParser.OptsBool(opts, "foreground");

        // Check if server is already running for this model — reuse if so
        bool serverAlreadyRunning = StateManager.IsServerRunning(modelName);

        if (!serverAlreadyRunning)
        {
            var cmd = LlamaServer.BuildLlamaCmd(cfg);

            Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " ...");

            ProcessStartInfo psi;
            bool isBackground = false; // true when using BuildBackgroundServerPsi

            if (foreground)
            {
                // Start in a new terminal window
                string? terminal = TerminalDetector.DetectTerminal();
                if (terminal is not null && TerminalDetector.BuildTerminalCommand(terminal, string.Join(" ", cmd.Select(CliParser.EscapeArg))) is string termArgs)
                {
                    psi = new ProcessStartInfo(terminal, termArgs)
                    {
                        UseShellExecute = true,
                    };
                }
                else
                {
                    Console.WriteLine("Terminal detection unavailable — starting server in background.");
                    foreground = false;
                    psi = LlamaServer.ServerPsi(cmd);
                }
            }
            else
            {
                // Start as a detached background process — streams are redirected
                // and drained so llama-server output doesn't clutter the veron console.
                psi = BuildBackgroundServerPsi(cmd);
                isBackground = true;
            }

            var serverProc = Process.Start(psi)
                            ?? throw new InvalidOperationException("Failed to start llama-server");

            // Start stream draining threads so the server doesn't block on full pipe buffers
            if (isBackground)
            {
                _ = Task.Run(() => { try { serverProc.StandardOutput.ReadToEnd(); } catch { } });
                _ = Task.Run(() => { try { serverProc.StandardError.ReadToEnd(); } catch { } });
            }

            string baseUrl = "http://localhost:" + cfg.Port;

            if (!LlamaServer.WaitForServer(baseUrl, cfg.Wait))
            {
                Console.Error.WriteLine("Error: server did not respond within " + cfg.Wait + "s");
                Environment.Exit(1);
            }

            Console.WriteLine("Server is ready at " + baseUrl);

            // Write server state file
            string fromName = Path.GetFileNameWithoutExtension(cfg.ModelPath);
            var serverState = new ServerState
            {
                Model = modelName,
                From = fromName,
                Port = cfg.Port,
                Context = cfg.Context,
                Pid = serverProc.Id,
                StartedAt = DateTime.UtcNow
            };
            StateManager.WriteState(serverState);

            // Also write the legacy PID file for backward compatibility
            PidManager.WritePid(serverProc.Id);
        }
        else
        {
            var existing = StateManager.GetState(modelName);
            Console.WriteLine("Server for " + modelName + " is already running (PID " +
                existing!.Pid + ", port " + existing.Port + ") — reusing.");
        }

        string baseUrl2 = "http://localhost:" + cfg.Port;

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

        // Pass the model alias so Claude Code knows which model it's using
        claudeArgs.Add("--model");
        claudeArgs.Add(cfg.Alias);

        if (toolConfigs is not null && toolConfigs.TryGetValue(toolName, out var toolCfg))
        {
            foreach (var (key, value) in toolCfg.Parameters)
            {
                var valErrors = ClaudeCodeValidator.ValidateClaudeCodeParameter(key, value);
                if (valErrors.Count > 0)
                {
                    Console.Error.WriteLine(valErrors[0]);
                    Environment.Exit(1);
                }

                claudeArgs.Add("--" + key);
                if (value is not null)
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
        claudePsi.EnvironmentVariables["ANTHROPIC_BASE_URL"]           = baseUrl2;
        claudePsi.EnvironmentVariables["CLAUDE_CODE_ATTRIBUTION_HEADER"] = "0";

        Console.WriteLine();
        Console.WriteLine("Launching claude code ...");
        Console.WriteLine("  ANTHROPIC_BASE_URL             = " + baseUrl2);
        Console.WriteLine("  CLAUDE_CODE_ATTRIBUTION_HEADER = 0");
        Console.WriteLine("  --model                          = " + cfg.Alias);

        if (toolConfigs is not null && toolConfigs.ContainsKey(toolName))
            Console.WriteLine("  TOOL claude-code args        = " + string.Join(" ", claudeArgs.Skip(3)));

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

        // Stop the server if we started it (not if it was already running)
        if (!serverAlreadyRunning)
        {
            Console.WriteLine();
            Console.WriteLine("Claude exited — stopping llama-server for " + modelName + " ...");
            StateManager.StopServer(modelName);
            PidManager.DeletePid();
        }
    }

    /// <summary>
    /// Builds a ProcessStartInfo for running llama-server as a detached background process.
    /// All streams are redirected so the server output doesn't clutter the veron console.
    /// Stream draining is handled by Task.Run(ReadToEnd) after the process starts.
    /// </summary>
    static ProcessStartInfo BuildBackgroundServerPsi(List<string> cmd)
    {
        return new ProcessStartInfo(cmd[0], string.Join(" ", cmd.Skip(1).Select(CliParser.EscapeArg)))
        {
            UseShellExecute   = false,
            CreateNoWindow    = true,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
    }
}
