using System;
using System.Collections.Generic;
using Veron.Daemon;

namespace Veron;

/// <summary>
/// `veron serve [name]` — runs the Veron server in the foreground. Models load on demand when a
/// request names them. With a name, that model is preloaded; if the server is already running,
/// only the preload happens.
/// </summary>
static class CmdServe
{
    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        string? preload = opts.GetValueOrDefault("model");

        var (host, port) = DaemonAddress.FromEnvironment();
        if (opts.GetValueOrDefault("host") is { Length: > 0 } h) host = h;
        if (opts.TryGetValue("port", out var p))
        {
            if (!int.TryParse(p, out port) || port is <= 0 or > 65535)
                Exit("invalid --port '" + p + "'");
        }

        var keepAlive = KeepAlive.Default;
        if (opts.TryGetValue("keep-alive", out var k) && !KeepAlive.TryParse(k, out keepAlive))
            Exit("invalid --keep-alive '" + k + "' (examples: 5m, 30s, 1h, 0, -1)");

        if (preload is not null && ModelfileParser.FindModelfile(preload) is null)
            Exit("no modelfile found for '" + preload + "' in " + Paths.ModelfilesDir);

        var client = new DaemonClient(host, port);
        if (client.IsRunning())
        {
            if (preload is null)
            {
                Console.WriteLine("Veron server is already running at " + client.BaseUrl);
                return;
            }
            Console.WriteLine("Veron server is already running — loading " + preload + " ...");
            try { client.Load(preload); }
            catch (DaemonException ex) { Exit(ex.Message); }
            Console.WriteLine(preload + " is ready at " + client.BaseUrl);
            return;
        }

        int code = DaemonHost.RunForeground(new DaemonOptions
        {
            Host      = host,
            Port      = port,
            KeepAlive = keepAlive,
            ModelsDir = modelsDir,
        }, preload);
        if (code != 0) Environment.Exit(code);
    }

    static void Exit(string message)
    {
        Console.Error.WriteLine("Error: " + message);
        Environment.Exit(1);
    }
}
