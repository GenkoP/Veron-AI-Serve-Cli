using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Veron;

static class CmdClaude
{
    const string DefaultClaudeBin = "claude";
    const string ToolName = "claude-code";

    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var model = ToolSession.ResolveOrExit(opts, modelsDir, "claude");

        // Build claude code CLI arguments; --model is the profile name (unique, unlike aliases)
        var claudeArgs = new List<string> { "code", "--model", model.Name };
        var toolCfg = LoadToolConfig(model.ModelfilePath);

        if (toolCfg is not null)
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
        }

        var client = ToolSession.ConnectOrExit(modelsDir);

        string claudeBin = opts.GetValueOrDefault("claude-bin", DefaultClaudeBin);
        var psi = new ProcessStartInfo(claudeBin) { UseShellExecute = false };
        foreach (var a in claudeArgs) psi.ArgumentList.Add(a);

        psi.Environment["ANTHROPIC_BASE_URL"]             = client.BaseUrl;
        psi.Environment["CLAUDE_CODE_ATTRIBUTION_HEADER"] = "0";
        // background (Haiku-class) requests go to the same local model
        psi.Environment["ANTHROPIC_DEFAULT_HAIKU_MODEL"]  = model.Name;
        psi.Environment["ANTHROPIC_SMALL_FAST_MODEL"]     = model.Name;

        Console.WriteLine();
        Console.WriteLine("Launching claude code ...");
        Console.WriteLine("  ANTHROPIC_BASE_URL             = " + client.BaseUrl);
        Console.WriteLine("  CLAUDE_CODE_ATTRIBUTION_HEADER = 0");
        Console.WriteLine("  ANTHROPIC_DEFAULT_HAIKU_MODEL  = " + model.Name);
        Console.WriteLine("  --model                        = " + model.Name);
        if (toolCfg is not null)
            Console.WriteLine("  TOOL claude-code args          = " + string.Join(" ", claudeArgs.Skip(3).Select(CliParser.EscapeArg)));
        Console.WriteLine();

        ToolSession.Run(client, model, "Claude", psi, claudeBin);
    }

    static ToolConfig? LoadToolConfig(string modelfilePath)
    {
        try
        {
            var configs = ModelfileParser.ParseToolBlocks(modelfilePath);
            return configs.TryGetValue(ToolName, out var cfg) ? cfg : null;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.Exit(1);
            return null;
        }
    }
}
