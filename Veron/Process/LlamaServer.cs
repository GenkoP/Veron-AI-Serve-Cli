using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Veron;

static class LlamaServer
{
    const string DefaultLlamaServer = "/home/genkop/Workspace/llama-cpp/llama.cpp/build/bin/llama-server";

    /// <summary>llama-server binary: $VERON_LLAMA_SERVER if set, otherwise the built-in default path.</summary>
    public static string BinaryPath =>
        Environment.GetEnvironmentVariable("VERON_LLAMA_SERVER") is { Length: > 0 } p ? p : DefaultLlamaServer;

    /// <summary>
    /// Builds the llama-server command line. The server always binds to 127.0.0.1 —
    /// clients reach it through the Veron daemon. PARAMETER args come last so they win.
    /// </summary>
    public static List<string> BuildLlamaCmd(ModelConfig cfg)
    {
        var cmd = new List<string>
        {
            BinaryPath,
            "-m", cfg.ModelPath,
            "--alias", cfg.Alias,
            "--host", "127.0.0.1",
            "--port", cfg.Port.ToString(CultureInfo.InvariantCulture),
            "-c", cfg.Context.ToString(CultureInfo.InvariantCulture),
            "-fa", cfg.Fa ? "on" : "off",
            "--repeat-penalty", cfg.RepeatPenalty.ToString("0.00", CultureInfo.InvariantCulture),
        };

        if (cfg.Jinja) cmd.Add("--jinja");

        if (cfg.NGpuLayers.HasValue) { cmd.Add("-ngl"); cmd.Add(cfg.NGpuLayers.Value.ToString(CultureInfo.InvariantCulture)); }
        if (cfg.BatchSize.HasValue)  { cmd.Add("-b");   cmd.Add(cfg.BatchSize.Value.ToString(CultureInfo.InvariantCulture)); }

        cmd.AddRange(cfg.ExtraArgs);
        return cmd;
    }

    /// <summary>ProcessStartInfo for a command list; arguments are passed without shell re-quoting.</summary>
    public static ProcessStartInfo ToPsi(IReadOnlyList<string> cmd)
    {
        var psi = new ProcessStartInfo(cmd[0])
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
        for (int i = 1; i < cmd.Count; i++) psi.ArgumentList.Add(cmd[i]);
        return psi;
    }
}
