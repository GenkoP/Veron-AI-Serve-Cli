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

    /// <summary>
    /// Runs <c>&lt;binary&gt; --version</c> and returns its version text, e.g. "1.1.0-dev (build 12815, commit 2a8bf0f59)".
    /// Null if the binary cannot be started, does not finish within <paramref name="timeoutMs"/>, or prints no version line.
    /// </summary>
    public static string? QueryVersion(string binary, int timeoutMs = 5000)
    {
        try
        {
            var psi = ToPsi(new[] { binary, "--version" });
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError  = true;
            using var p = Process.Start(psi);
            if (p is null) return null;

            // llama.cpp prints the version to stderr; read both streams so neither pipe can block the child
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
            return ParseVersion(stdout.Result + "\n" + stderr.Result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The text after "version:" in llama.cpp's <c>--version</c> output, or null if there is none.</summary>
    public static string? ParseVersion(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("version:", StringComparison.Ordinal)) continue;
            string v = line["version:".Length..].Trim();
            return v.Length > 0 ? v : null;
        }
        return null;
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
