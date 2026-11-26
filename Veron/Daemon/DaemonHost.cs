using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veron.Daemon;

public sealed class DaemonOptions
{
    public string   Host          { get; set; } = DaemonAddress.DefaultHost;
    public int      Port          { get; set; } = DaemonAddress.DefaultPort;
    public TimeSpan KeepAlive     { get; set; } = Veron.KeepAlive.Default;
    public string   ModelfilesDir { get; set; } = Paths.ModelfilesDir;
    public string   ModelsDir     { get; set; } = "";
    public string   LogsDir       { get; set; } = Paths.LogsDir;
    public TimeSpan TickInterval  { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>Written to ~/.veron/daemon.json while the daemon runs, so the CLI can find it.</summary>
public sealed record DaemonState(int Pid, string Host, int Port, DateTimeOffset StartedAt);

record ModelRequest(string? Model);
record SessionRequest(string? Model, int Pid);

public static class DaemonHost
{
    /// <summary>Builds the daemon web app (not started). Tests pass their own registry/manager and port 0.</summary>
    public static WebApplication Build(DaemonOptions opts, IModelRegistry registry, ModelManager manager, Action<string> log)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{opts.Host}:{opts.Port}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = null);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

        var app   = builder.Build();
        var http  = ProxyEndpoint.CreateHttpClient();
        var proxy = new ProxyEndpoint(registry, manager, http, log);
        var stats = new StatsCollector(manager);

        app.MapGet("/health", () => Results.Json(new { status = "ok", service = "veron" }));

        app.MapGet("/v1/models", () => Results.Json(new
        {
            @object = "list",
            data = registry.List().Select(m => new
            {
                id       = m.Name,
                @object  = "model",
                created  = 0,
                owned_by = "veron",
                alias    = m.Config.Alias,
            }),
        }));

        app.MapGet("/veron/ps", () => Results.Json(manager.Status()));

        app.MapGet("/veron/stats", async (HttpContext ctx) =>
        {
            var snap = await stats.GetAsync(ctx.RequestAborted);
            return Results.Json(snap);
        });

        app.MapPost("/veron/load", async (ModelRequest req, HttpContext ctx) =>
        {
            if (registry.Resolve(req.Model ?? "") is not { } model)
                return NotFound(registry, req.Model);
            try
            {
                using (await manager.AcquireAsync(model, ctx.RequestAborted)) { }
                return Results.Json(new { model = model.Name, status = manager.Status() });
            }
            catch (ModelLoadException ex) { return Error(503, "api_error", ex.Message); }
        });

        app.MapPost("/veron/unload", async (HttpContext ctx) =>
        {
            string? name = null;
            if (ctx.Request.ContentLength is > 0)
                name = (await JsonSerializer.DeserializeAsync<ModelRequest>(ctx.Request.Body, JsonOpts))?.Model;
            var unloaded = await manager.UnloadAsync(string.IsNullOrEmpty(name) ? null : name);
            return Results.Json(new { unloaded });
        });

        app.MapPost("/veron/sessions", async (SessionRequest req, HttpContext ctx) =>
        {
            if (registry.Resolve(req.Model ?? "") is not { } model)
                return NotFound(registry, req.Model);
            try
            {
                var s = await manager.CreateSessionAsync(model, req.Pid, ctx.RequestAborted);
                return Results.Json(new { id = s.Id, model = s.Model, wasAlreadyLoaded = s.WasAlreadyLoaded });
            }
            catch (ModelLoadException ex) { return Error(503, "api_error", ex.Message); }
        });

        app.MapDelete("/veron/sessions/{id}", async (string id) =>
        {
            var r = await manager.ReleaseSessionAsync(id);
            return Results.Json(new { unloaded = r.Unloaded, unloadPending = r.UnloadPending });
        });

        app.MapPost("/veron/shutdown", (IHostApplicationLifetime life) =>
        {
            log("shutdown requested");
            _ = Task.Run(async () => { await Task.Delay(100); life.StopApplication(); });
            return Results.Json(new { status = "stopping" });
        });

        // Everything else is an inference API call for llama-server
        app.MapFallback(proxy.HandleAsync);

        // Maintenance loop: idle unload + dead-session cleanup
        var tickCts = new CancellationTokenSource();
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(opts.TickInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(tickCts.Token))
                {
                    try { manager.Tick(); } catch (Exception ex) { log("tick failed: " + ex.Message); }
                }
            }
            catch (OperationCanceledException) { }
        }));
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            tickCts.Cancel();
            log("stopping — unloading models");
            manager.Dispose();
            http.Dispose();
            stats.Dispose();
        });

        return app;
    }

    static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    static IResult NotFound(IModelRegistry registry, string? name) =>
        Error(404, "not_found_error", registry.ProfileError(name ?? "") ?? $"Model '{name}' not found");

    static IResult Error(int status, string type, string message) =>
        Results.Json(new { type = "error", error = new { type, message } }, statusCode: status);

    /// <summary>`veron serve`: runs the daemon until Ctrl+C / SIGTERM / `veron shutdown`.</summary>
    public static int RunForeground(DaemonOptions opts, string? preload)
    {
        static void Log(string msg) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {msg}");

        var registry = new ModelRegistry(opts.ModelfilesDir, opts.ModelsDir);
        var manager  = new ModelManager(new LlamaLauncher(), opts.KeepAlive, opts.LogsDir, log: Log);
        var app      = Build(opts, registry, manager, Log);

        try { app.StartAsync().GetAwaiter().GetResult(); }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Error: cannot listen on {opts.Host}:{opts.Port}: {ex.Message}");
            manager.Dispose();
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Paths.DaemonStateFile)!);
        var state = new DaemonState(Environment.ProcessId, opts.Host, opts.Port, DateTimeOffset.Now);
        File.WriteAllText(Paths.DaemonStateFile, JsonSerializer.Serialize(state, JsonOpts));

        string keep = KeepAlive.Format(opts.KeepAlive);
        Log($"Veron server listening on http://{opts.Host}:{opts.Port} (keep-alive {keep}, llama-server: {LlamaServer.BinaryPath})");
        Log($"profiles: {opts.ModelfilesDir}  models: {opts.ModelsDir}  logs: {opts.LogsDir}");

        if (preload is not null)
        {
            _ = Task.Run(async () =>
            {
                if (registry.Resolve(preload) is not { } m) { Log($"preload: model '{preload}' not found"); return; }
                try { using (await manager.AcquireAsync(m, CancellationToken.None)) { } }
                catch (ModelLoadException ex) { Log(ex.Message); }
            });
        }

        try { app.WaitForShutdownAsync().GetAwaiter().GetResult(); }
        finally
        {
            try
            {
                // only remove the state file if it is still ours
                var json = File.ReadAllText(Paths.DaemonStateFile);
                if (JsonSerializer.Deserialize<DaemonState>(json, JsonOpts)?.Pid == Environment.ProcessId)
                    File.Delete(Paths.DaemonStateFile);
            }
            catch { }
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Log("Veron server stopped.");
        }
        return 0;
    }
}
