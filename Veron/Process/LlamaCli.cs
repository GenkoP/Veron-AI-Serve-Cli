using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Veron;

static class LlamaCli
{
    const string DefaultLlamaCli = "/home/genkop/Workspace/llama-cpp/llama.cpp/build/bin/llama-cli";

    public static List<string> BuildLlamaCmd(ModelConfig cfg)
    {
        var cmd = new List<string>
        {
            DefaultLlamaCli,
            "-m", cfg.ModelPath,
            "--alias", cfg.Alias,
            "-ngl", (cfg.NGpuLayers ?? -1).ToString(),
            "-c", cfg.Context.ToString(),
            "--repeat-penalty", cfg.RepeatPenalty.ToString("0.00"),
        };

        if (cfg.Fa) cmd.Add("--flash-attn");
        if (cfg.Jinja) cmd.Add("--jinja");
        if (cfg.Color ?? true) cmd.Add("--color");

        cmd.AddRange(new[] { "--temperature", (cfg.Temperature ?? 0.8f).ToString("0.00") });

        cmd.AddRange(new[] { "--top-p", (cfg.TopP ?? 0.9f).ToString("0.00") });

        if (cfg.Prompt is not null)
        {
            cmd.AddRange(new[] { "--prompt", cfg.Prompt });
            cmd.Add("--single-turn");
        }

        return cmd;
    }

    public static ProcessStartInfo CliPsi(List<string> cmd)
    {
        return new ProcessStartInfo(cmd[0], string.Join(" ", cmd.Skip(1).Select(CliParser.EscapeArg)))
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
    }
}
