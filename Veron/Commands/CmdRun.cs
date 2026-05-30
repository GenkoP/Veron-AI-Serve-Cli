using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Veron;

static class CmdRun
{
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
}
