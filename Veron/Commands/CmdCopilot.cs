using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Veron;

static class CmdCopilot
{
    const string DefaultCopilotBin = "copilot";

    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = ModelfileParser.LoadConfig(opts, modelsDir, out string? modelfilePath);

        // The model profile name the user typed
        string modelName = opts.GetValueOrDefault("model") ?? throw new ArgumentNullException("model argument required");

        // Check if server is already running for this model — reuse if so
        bool serverAlreadyRunning = StateManager.IsServerRunning(modelName);

        if (!serverAlreadyRunning)
        {
            var cmd = LlamaServer.BuildLlamaCmd(cfg);

            Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " ...");

            var psi = BuildBackgroundServerPsi(cmd);

            var serverProc = Process.Start(psi)
                            ?? throw new InvalidOperationException("Failed to start llama-server");

            // Start stream draining threads so the server doesn't block on full pipe buffers
            _ = Task.Run(() => { try { serverProc.StandardOutput.ReadToEnd(); } catch { } });
            _ = Task.Run(() => { try { serverProc.StandardError.ReadToEnd(); } catch { } });

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
        string toolName = "copilot";
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

        // Build copilot CLI arguments
        var copilotArgs = new List<string>();

        // Pass the model alias so Copilot knows which model it's using
        copilotArgs.Add("--model");
        copilotArgs.Add(cfg.Alias);

        // Handle --prompt for non-interactive mode
        string? promptText = opts.GetValueOrDefault("prompt");
        if (promptText is not null)
        {
            copilotArgs.Add("-p");
            copilotArgs.Add(promptText);
        }

        // Apply TOOL copilot parameters from modelfile
        if (toolConfigs is not null && toolConfigs.TryGetValue(toolName, out var toolCfg))
        {
            foreach (var (key, value) in toolCfg.Parameters)
            {
                var valErrors = CopilotValidator.ValidateCopilotParameter(key, value);
                if (valErrors.Count > 0)
                {
                    Console.Error.WriteLine(valErrors[0]);
                    Environment.Exit(1);
                }

                copilotArgs.Add("--" + key);
                if (value is not null)
                    copilotArgs.Add(value);
            }

            Console.WriteLine("Using TOOL copilot config from modelfile");
        }

        // Launch copilot
        string copilotBin = opts.GetValueOrDefault("copilot-bin", DefaultCopilotBin);
        var copilotPsi = new ProcessStartInfo(copilotBin, string.Join(" ", copilotArgs.Select(CliParser.EscapeArg)))
        {
            UseShellExecute = false,
        };

        // Set BYOK environment variables
        copilotPsi.EnvironmentVariables["COPILOT_PROVIDER_TYPE"]     = "openai";
        copilotPsi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"] = baseUrl2 + "/v1";
        copilotPsi.EnvironmentVariables["COPILOT_MODEL"]             = cfg.Alias;
        copilotPsi.EnvironmentVariables["COPILOT_OFFLINE"]           = "true";

        Console.WriteLine();
        Console.WriteLine("Launching copilot ...");
        Console.WriteLine("  COPILOT_PROVIDER_TYPE     = openai");
        Console.WriteLine("  COPILOT_PROVIDER_BASE_URL = " + baseUrl2 + "/v1");
        Console.WriteLine("  COPILOT_MODEL             = " + cfg.Alias);
        Console.WriteLine("  COPILOT_OFFLINE           = true");

        if (promptText is not null)
            Console.WriteLine("  --prompt                    = " + promptText);

        if (toolConfigs is not null && toolConfigs.ContainsKey(toolName))
            Console.WriteLine("  TOOL copilot args         = " + string.Join(" ", copilotArgs.Skip(2)));

        Console.WriteLine();

        try
        {
            var copilotProc = Process.Start(copilotPsi)
                                 ?? throw new FileNotFoundException(copilotBin);
            copilotProc.WaitForExit();
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is Win32Exception)
        {
            Console.Error.WriteLine("Error: couldn't launch '" + copilotBin + "' — make sure it's in $PATH.");
            Environment.Exit(1);
        }

        // Stop the server if we started it (not if it was already running)
        if (!serverAlreadyRunning)
        {
            Console.WriteLine();
            Console.WriteLine("Copilot exited — stopping llama-server for " + modelName + " ...");
            StateManager.StopServer(modelName);
            PidManager.DeletePid();
        }
    }

    /// <summary>
    /// Builds a ProcessStartInfo for running llama-server as a detached background process.
    /// All streams are redirected so the server output doesn't clutter the veron console.
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
