using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Veron.Daemon;

namespace Veron;

/// <summary>
/// Shared flow for `veron claude` / `veron copilot`: resolve the profile, make sure the daemon
/// runs, open a session (loads the model), run the tool, close the session (unloads the model
/// if this session loaded it and no other session still uses it).
/// </summary>
static class ToolSession
{
    /// <summary>Resolves a profile or exits with an error message.</summary>
    public static ResolvedModel ResolveOrExit(Dictionary<string, string> opts, string modelsDir, string command)
    {
        string? name = opts.GetValueOrDefault("model");
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine($"Error: '{command}' requires a model profile name");
            Console.Error.WriteLine($"Usage: veron {command} <name>");
            Environment.Exit(1);
        }

        string? path = ModelfileParser.FindModelfile(name);
        if (path is null)
        {
            Console.Error.WriteLine("Error: no modelfile found for '" + name + "' in " + Paths.ModelfilesDir);
            Environment.Exit(1);
        }

        try
        {
            var cfg = ModelfileParser.ParseModelfile(path, modelsDir);
            return new ResolvedModel(System.IO.Path.GetFileName(path), path, cfg);
        }
        catch (Exception ex) when (ex is System.IO.IOException or FormatException)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            Environment.Exit(1);
            throw; // unreachable
        }
    }

    /// <summary>Starts the daemon if needed or exits with an error message.</summary>
    public static DaemonClient ConnectOrExit(string modelsDir)
    {
        var client = DaemonClient.Default();
        try { client.EnsureRunning(modelsDir); }
        catch (DaemonException ex) { Console.Error.WriteLine("Error: " + ex.Message); Environment.Exit(1); }
        return client;
    }

    public static void Run(DaemonClient client, ResolvedModel model, string toolLabel, ProcessStartInfo psi, string bin)
    {
        Console.WriteLine("Loading " + model.Name + " ...");
        string sessionId;
        try { sessionId = client.OpenSession(model.Name, Environment.ProcessId); }
        catch (DaemonException ex) { Console.Error.WriteLine("Error: " + ex.Message); Environment.Exit(1); return; }
        Console.WriteLine(model.Name + " is ready (Veron server " + client.BaseUrl + ")");

        // The tool owns Ctrl+C while it runs; veron must survive to close the session.
        ConsoleCancelEventHandler ignore = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += ignore;
        int exitCode = 0;
        try
        {
            using var proc = Process.Start(psi) ?? throw new Win32Exception("Process.Start returned null");
            proc.WaitForExit();
            exitCode = proc.ExitCode;
        }
        catch (Win32Exception)
        {
            Console.Error.WriteLine("Error: couldn't launch '" + bin + "' — make sure it's in $PATH.");
            exitCode = 1;
        }
        finally
        {
            Console.CancelKeyPress -= ignore;
            Console.WriteLine();
            try
            {
                var r = client.CloseSession(sessionId);
                if (r.Unloaded)      Console.WriteLine(toolLabel + " exited — unloaded " + model.Name + ".");
                else if (r.UnloadPending) Console.WriteLine(toolLabel + " exited — " + model.Name + " unloads when its last request finishes.");
                else                 Console.WriteLine(toolLabel + " exited — " + model.Name + " stays loaded (in use elsewhere).");
            }
            catch (DaemonException ex) { Console.Error.WriteLine("Warning: " + ex.Message); }
        }

        if (exitCode != 0) Environment.Exit(exitCode);
    }
}
