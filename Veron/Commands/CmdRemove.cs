using System;
using System.Collections.Generic;
using System.IO;

namespace Veron;

static class CmdRemove
{
    public static string ConfirmationPrompt(string modelName) =>
        "Remove profile " + modelName + "? [y/N]";

    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        string? modelName = opts.GetValueOrDefault("model");
        if (string.IsNullOrEmpty(modelName))
        {
            Console.Error.WriteLine("Error: 'remove' requires a model profile name");
            Console.Error.WriteLine("Usage: veron remove <name>");
            Environment.Exit(1);
        }

        bool force = CliParser.OptsBool(opts, "force") || CliParser.OptsBool(opts, "f");

        string? mfPath = ModelfileParser.FindModelfile(modelName);
        if (mfPath is null)
        {
            Console.Error.WriteLine("Error: profile '" + modelName + "' not found in " + Paths.ModelfilesDir);
            Environment.Exit(1);
        }

        if (!force)
        {
            Console.Write(ConfirmationPrompt(modelName) + " ");
            string? response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("Cancelled.");
                return;
            }
        }

        if (StateManager.IsServerRunning(modelName))
        {
            Console.WriteLine("Stopping llama-server for " + modelName + " ...");
            StateManager.StopServer(modelName);
        }

        File.Delete(mfPath);
        Console.WriteLine("Removed profile '" + modelName + "' (" + mfPath + ")");

        StateManager.DeleteState(modelName);
    }
}
