using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Veron.Daemon;

/// <summary>Where the daemon listens: $VERON_HOST, else 127.0.0.1:11435 (Ollama uses 11434).</summary>
public static class DaemonAddress
{
    public const string DefaultHost = "127.0.0.1";
    public const int    DefaultPort = 11435;

    /// <summary>Parses "host", "host:port", ":port" or "http://host:port".</summary>
    public static (string Host, int Port) Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (DefaultHost, DefaultPort);
        string s = value.Trim();
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = s[7..];
        s = s.TrimEnd('/');

        int colon = s.LastIndexOf(':');
        if (colon < 0) return (s, DefaultPort);
        string host = colon == 0 ? DefaultHost : s[..colon];
        if (!int.TryParse(s[(colon + 1)..], out int port) || port is <= 0 or > 65535)
            throw new FormatException("Invalid VERON_HOST: '" + value + "'");
        return (host, port);
    }

    public static (string Host, int Port) FromEnvironment() =>
        Parse(Environment.GetEnvironmentVariable("VERON_HOST"));
}

public sealed class DaemonException(string message) : Exception(message);

/// <summary>CLI-side client for the Veron daemon's admin API.</summary>
public sealed class DaemonClient
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Host { get; }
    public int    Port { get; }
    public string BaseUrl => $"http://{(Host is "0.0.0.0" or "::" ? "127.0.0.1" : Host)}:{Port}";

    public DaemonClient(string host, int port) { Host = host; Port = port; }

    /// <summary>
    /// $VERON_HOST wins; otherwise the address of a running daemon from ~/.veron/daemon.json
    /// (it may have been started with --port); otherwise the default address.
    /// </summary>
    public static DaemonClient Default()
    {
        if (Environment.GetEnvironmentVariable("VERON_HOST") is { Length: > 0 })
        {
            var (h, p) = DaemonAddress.FromEnvironment();
            return new DaemonClient(h, p);
        }
        try
        {
            var state = JsonSerializer.Deserialize<DaemonState>(File.ReadAllText(Paths.DaemonStateFile), Json);
            if (state is not null && IsPidAlive(state.Pid)) return new DaemonClient(state.Host, state.Port);
        }
        catch { }
        return new DaemonClient(DaemonAddress.DefaultHost, DaemonAddress.DefaultPort);
    }

    HttpClient Http(TimeSpan timeout) => new() { BaseAddress = new Uri(BaseUrl), Timeout = timeout };

    public bool IsRunning()
    {
        try
        {
            using var http = Http(TimeSpan.FromSeconds(1));
            using var r = http.GetAsync("/health").GetAwaiter().GetResult();
            if (!r.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(r.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return doc.RootElement.TryGetProperty("service", out var s) && s.GetString() == "veron";
        }
        catch { return false; }
    }

    /// <summary>The version the daemon reports in /health; null when unreachable or when it predates versioning.</summary>
    public string? DaemonVersion()
    {
        try
        {
            using var http = Http(TimeSpan.FromSeconds(1));
            using var r = http.GetAsync("/health").GetAwaiter().GetResult();
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(r.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>Starts the daemon in the background (detached) unless it is already running.</summary>
    public void EnsureRunning(string modelsDir)
    {
        if (IsRunning()) return;

        Console.WriteLine("Starting Veron server on " + BaseUrl + " ...");
        Directory.CreateDirectory(Paths.LogsDir);

        var self = SelfCommand();
        var psi  = new ProcessStartInfo("setsid") { UseShellExecute = false };
        // setsid -f: new session, so the daemon survives this terminal closing
        foreach (var a in new[] { "-f", "/bin/sh", "-c", "exec \"$@\" >>\"$VERON_LOG\" 2>&1 </dev/null", "sh" })
            psi.ArgumentList.Add(a);
        foreach (var a in self) psi.ArgumentList.Add(a);
        foreach (var a in new[] { "serve", "--models-dir", modelsDir, "--host", Host, "--port", Port.ToString() })
            psi.ArgumentList.Add(a);
        psi.Environment["VERON_LOG"] = Paths.ServerLogFile;

        try { using var p = Process.Start(psi); p?.WaitForExit(5000); }
        catch (System.ComponentModel.Win32Exception ex)
        { throw new DaemonException("Cannot start the Veron server (setsid): " + ex.Message); }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (IsRunning()) return;
            Thread.Sleep(250);
        }
        throw new DaemonException("Veron server did not start — see " + Paths.ServerLogFile);
    }

    /// <summary>The command that runs this veron binary (apphost, or `dotnet Veron.dll`).</summary>
    static List<string> SelfCommand()
    {
        string exe = Environment.ProcessPath ?? throw new DaemonException("Cannot determine veron executable path");
        var cmd = new List<string> { exe };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            Assembly.GetEntryAssembly()?.Location is { Length: > 0 } dll)
            cmd.Add(dll);
        return cmd;
    }

    // ── Admin API ────────────────────────────────────────────────────────

    public void Load(string model) =>
        Send(HttpMethod.Post, "/veron/load", new { model }, TimeSpan.FromMinutes(30));

    public IReadOnlyList<string> Unload(string? model)
    {
        var doc = Send(HttpMethod.Post, "/veron/unload", model is null ? new { } : new { model }, TimeSpan.FromMinutes(1));
        return doc.RootElement.GetProperty("unloaded").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    public List<ModelStatus> Ps()
    {
        var doc = Send(HttpMethod.Get, "/veron/ps", null, TimeSpan.FromSeconds(10));
        return doc.RootElement.Deserialize<List<ModelStatus>>(Json) ?? new();
    }

    public StatsSnapshot Stats()
    {
        var doc = Send(HttpMethod.Get, "/veron/stats", null, TimeSpan.FromSeconds(5));
        return doc.RootElement.Deserialize<StatsSnapshot>(Json)
            ?? throw new DaemonException("Empty stats response from the Veron server");
    }

    public string OpenSession(string model, int pid)
    {
        var doc = Send(HttpMethod.Post, "/veron/sessions", new { model, pid }, TimeSpan.FromMinutes(30));
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public SessionReleaseResult CloseSession(string id)
    {
        var doc = Send(HttpMethod.Delete, "/veron/sessions/" + Uri.EscapeDataString(id), null, TimeSpan.FromMinutes(1));
        return new SessionReleaseResult(doc.RootElement.GetProperty("unloaded").GetBoolean(),
                                        doc.RootElement.GetProperty("unloadPending").GetBoolean());
    }

    public void Shutdown() => Send(HttpMethod.Post, "/veron/shutdown", new { }, TimeSpan.FromSeconds(10));

    JsonDocument Send(HttpMethod method, string path, object? body, TimeSpan timeout)
    {
        using var http = Http(timeout);
        using var req  = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = JsonContent.Create(body);

        HttpResponseMessage resp;
        try { resp = http.SendAsync(req).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { throw new DaemonException("Cannot reach the Veron server at " + BaseUrl + ": " + ex.Message); }

        using (resp)
        {
            string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) throw new DaemonException(ErrorMessage(text, (int)resp.StatusCode));
            return JsonDocument.Parse(text);
        }
    }

    static string ErrorMessage(string body, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                return m.GetString() ?? ("HTTP " + status);
        }
        catch { }
        return "HTTP " + status + (body.Length > 0 ? ": " + body : "");
    }

    static bool IsPidAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }
}
