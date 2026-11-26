using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Veron;

static class CmdCopilot
{
    const string DefaultCopilotBin = "copilot";
    const string ToolName = "copilot";

    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var model = ToolSession.ResolveOrExit(opts, modelsDir, "copilot");

        // Build copilot CLI arguments; --model is the profile name (unique, unlike aliases)
        var copilotArgs = new List<string> { "--model", model.Name };

        // Handle --prompt for non-interactive mode
        string? promptText = opts.GetValueOrDefault("prompt");
        if (promptText is not null)
        {
            copilotArgs.Add("-p");
            copilotArgs.Add(promptText);
        }

        ToolConfig? toolCfg = null;
        try
        {
            var configs = ModelfileParser.ParseToolBlocks(model.ModelfilePath);
            configs.TryGetValue(ToolName, out toolCfg);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.Exit(1);
        }

        if (toolCfg is not null)
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
        }

        var client = ToolSession.ConnectOrExit(modelsDir);

        string copilotBin = opts.GetValueOrDefault("copilot-bin", DefaultCopilotBin);
        var psi = new ProcessStartInfo(copilotBin) { UseShellExecute = false };
        foreach (var a in copilotArgs) psi.ArgumentList.Add(a);

        // Set BYOK environment variables
        psi.Environment["COPILOT_PROVIDER_TYPE"]     = "openai";
        psi.Environment["COPILOT_PROVIDER_BASE_URL"] = client.BaseUrl + "/v1";
        psi.Environment["COPILOT_MODEL"]             = model.Name;
        // psi.Environment["COPILOT_OFFLINE"]           = "true";

        Console.WriteLine();
        Console.WriteLine("Launching copilot ...");
        Console.WriteLine("  COPILOT_PROVIDER_TYPE     = openai");
        Console.WriteLine("  COPILOT_PROVIDER_BASE_URL = " + client.BaseUrl + "/v1");
        Console.WriteLine("  COPILOT_MODEL             = " + model.Name);
        if (promptText is not null)
            Console.WriteLine("  --prompt                  = " + promptText);
        if (toolCfg is not null)
            Console.WriteLine("  TOOL copilot args         = " +
                              string.Join(" ", copilotArgs.Skip(promptText is null ? 2 : 4).Select(CliParser.EscapeArg)));
        Console.WriteLine();

        ToolSession.Run(client, model, "Copilot", psi, copilotBin);
    }
}
