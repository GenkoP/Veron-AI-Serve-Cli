using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;

namespace Veron;

static class CmdServe
{
    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = ModelfileParser.LoadConfig(opts, modelsDir, out _);

        // The model profile name is what the user typed (e.g., "qwopus")
        string modelName = opts.GetValueOrDefault("model") ?? throw new ArgumentNullException("model argument required");

        // Check if server is already running for this model
        if (StateManager.IsServerRunning(modelName))
        {
            var existing = StateManager.GetState(modelName);
            Console.WriteLine("Server for " + modelName + " is already running (PID " +
                existing!.Pid + ", port " + existing.Port + ")");
            return;
        }

        var cmd = LlamaServer.BuildLlamaCmd(cfg);

        Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " …");
        Console.WriteLine("  Command: " + string.Join(" ", cmd.Select(CliParser.EscapeArg)));

        var psi = LlamaServer.ServerPsi(cmd);
        var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start llama-server");

        // Write server state file
        string fromName = Path.GetFileNameWithoutExtension(cfg.ModelPath);
        var serverState = new ServerState
        {
            Model = modelName,
            From = fromName,
            Port = cfg.Port,
            Context = cfg.Context,
            Pid = proc.Id,
            StartedAt = DateTime.UtcNow
        };
        StateManager.WriteState(serverState);

        // Also write the legacy PID file for backward compatibility
        PidManager.WritePid(proc.Id);

        string baseUrl = "http://localhost:" + cfg.Port;

        if (LlamaServer.WaitForServer(baseUrl, cfg.Wait))
            Console.WriteLine("Server is ready at " + baseUrl);
        else
            Console.Error.WriteLine("Warning: server did not respond within " + cfg.Wait + "s");

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

            // Clean up state on exit
            StateManager.DeleteState(modelName);
            PidManager.DeletePid();
        }
    }
}
