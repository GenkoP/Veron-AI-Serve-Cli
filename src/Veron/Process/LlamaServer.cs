using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Veron;

static class LlamaServer
{
    const string DefaultLlamaServer = "/home/genkop/Workspace/llama-cpp/llama.cpp/build/bin/llama-server";

    public static List<string> BuildLlamaCmd(ModelConfig cfg)
    {
        var cmd = new List<string>
        {
            DefaultLlamaServer,
            "-m", cfg.ModelPath,
            "--alias", cfg.Alias,
            "--port", cfg.Port.ToString(),
            "-c", cfg.Context.ToString(),
            "-fa", cfg.Fa ? "1" : "0",
            "--repeat-penalty", cfg.RepeatPenalty.ToString("0.00"),
        };

        if (cfg.Jinja) cmd.Add("--jinja");

        if (cfg.NGpuLayers.HasValue) { cmd.Add("-ngl"); cmd.Add(cfg.NGpuLayers.Value.ToString()); }
        if (cfg.BatchSize.HasValue)  { cmd.Add("-b");   cmd.Add(cfg.BatchSize.Value.ToString()); }

        return cmd;
    }

    public static ProcessStartInfo ServerPsi(List<string> cmd)
    {
        return new ProcessStartInfo(cmd[0], string.Join(" ", cmd.Skip(1).Select(CliParser.EscapeArg)))
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
    }

    public static bool WaitForServer(string baseUrl, int timeoutSec)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var r = http.GetAsync(baseUrl + "/health").GetAwaiter().GetResult();
                if (r.IsSuccessStatusCode) return true;
            }
            catch { /* not ready yet */ }
            Task.Delay(1000).GetAwaiter().GetResult();
        }
        return false;
    }
}
