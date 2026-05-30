using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Veron;

static class CmdServe
{
    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = ModelfileParser.LoadConfig(opts, modelsDir, out _);

        var cmd = LlamaServer.BuildLlamaCmd(cfg);

        Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " …");
        Console.WriteLine("  Command: " + string.Join(" ", cmd.Select(CliParser.EscapeArg)));

        var psi = LlamaServer.ServerPsi(cmd);
        var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start llama-server");

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
        }
    }
}
