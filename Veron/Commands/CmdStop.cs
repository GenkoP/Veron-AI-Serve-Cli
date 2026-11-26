using System;
using System.Collections.Generic;
using System.Threading;
using Veron.Daemon;

namespace Veron;

/// <summary>`veron stop [name]` unloads models; `veron shutdown` stops the Veron server itself.</summary>
static class CmdStop
{
    public static void Run(Dictionary<string, string> opts)
    {
        string? modelName = opts.GetValueOrDefault("model");

        var client = DaemonClient.Default();
        if (!client.IsRunning())
        {
            Console.WriteLine("Veron server is not running.");
            return;
        }

        try
        {
            var unloaded = client.Unload(modelName);
            if (unloaded.Count > 0)
                foreach (var name in unloaded) Console.WriteLine("Unloaded " + name + ".");
            else
                Console.WriteLine(modelName is null ? "No models loaded." : modelName + " is not loaded.");
        }
        catch (DaemonException ex) { Console.Error.WriteLine("Error: " + ex.Message); Environment.Exit(1); }
    }

    public static void Shutdown()
    {
        var client = DaemonClient.Default();
        if (!client.IsRunning())
        {
            Console.WriteLine("Veron server is not running.");
            return;
        }

        try { client.Shutdown(); }
        catch (DaemonException ex) { Console.Error.WriteLine("Error: " + ex.Message); Environment.Exit(1); }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && client.IsRunning()) Thread.Sleep(250);
        Console.WriteLine(client.IsRunning() ? "Veron server is still shutting down." : "Veron server stopped.");
    }
}
