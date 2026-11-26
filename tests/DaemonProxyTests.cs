using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Veron;
using Veron.Daemon;
using Xunit;

namespace Veron.Tests;

/// <summary>Launches a tiny Kestrel app that behaves like llama-server, instead of a real process.</summary>
sealed class FakeLlamaServerLauncher : ILlamaLauncher
{
    public ConcurrentQueue<string> Started { get; } = new();
    public ConcurrentQueue<(string Model, string Path, string Body, string? Auth)> Requests { get; } = new();
    public TaskCompletionSource SlowRequestAborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseStream { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool FailStartup { get; set; }
    public string? SlotsJson { get; set; }
    public string? LogText { get; set; }

    public ILlamaProcess Start(ResolvedModel model, int port, string logPath)
    {
        Started.Enqueue(model.Name);
        if (LogText is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, LogText);
        }
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:" + port);
        var app = builder.Build();

        app.MapGet("/health", () => FailStartup ? Results.StatusCode(503) : Results.Ok(new { status = "ok" }));

        app.MapGet("/slots", () => Results.Text(SlotsJson ?? "[]", "application/json"));

        // streams 3 SSE events; the last one only after the test releases it
        app.MapPost("/v1/chat/completions", async (HttpContext ctx) =>
        {
            string body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            Requests.Enqueue((model.Name, ctx.Request.Path + ctx.Request.QueryString, body, ctx.Request.Headers.Authorization));
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers["X-Upstream-Model"] = model.Name;
            await ctx.Response.WriteAsync("data: {\"n\":1}\n\n");
            await ctx.Response.Body.FlushAsync();
            await ctx.Response.WriteAsync("data: {\"n\":2}\n\n");
            await ctx.Response.Body.FlushAsync();
            await ReleaseStream.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ctx.Response.WriteAsync("data: [DONE]\n\n");
        });

        app.MapPost("/v1/messages", async (HttpContext ctx) =>
        {
            string body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            Requests.Enqueue((model.Name, ctx.Request.Path + ctx.Request.QueryString, body, ctx.Request.Headers["x-api-key"]));
            return Results.Json(new { type = "message", served_by = model.Name }, statusCode: 201);
        });

        app.MapPost("/slow", async (HttpContext ctx) =>
        {
            await ctx.Response.WriteAsync("data: first\n\n");
            await ctx.Response.Body.FlushAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(30), ctx.RequestAborted); }
            catch (OperationCanceledException) { SlowRequestAborted.TrySetResult(); }
        });

        app.StartAsync().GetAwaiter().GetResult();
        return new Proc(app, port, logPath);
    }

    sealed class Proc(WebApplication app, int port, string logPath) : ILlamaProcess
    {
        static int _pid = 5000;
        public int Pid { get; } = Interlocked.Increment(ref _pid);
        public int Port { get; } = port;
        public string LogPath { get; } = logPath;
        public bool HasExited { get; private set; }
        public event Action? Exited { add { } remove { } }

        public async Task WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var http = new HttpClient();
            var r = await http.GetAsync($"http://127.0.0.1:{Port}/health", ct);
            if (!r.IsSuccessStatusCode) throw new ModelLoadException("health check failed");
        }

        public void Kill()
        {
            if (HasExited) return;
            HasExited = true;
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

public sealed class DaemonProxyTests : IAsyncLifetime
{
    readonly TempProfiles _profiles = new();
    readonly FakeLlamaServerLauncher _launcher = new();
    readonly List<string> _log = new();
    ModelManager _manager = null!;
    WebApplication _app = null!;
    HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _profiles.Add("alpha", "alpha.gguf", "PARAMETER alias \"Alpha-27B\"");
        _profiles.Add("beta", "beta.gguf");

        _manager = new ModelManager(_launcher, TimeSpan.FromMinutes(5), _profiles.LogsDir, log: s => { lock (_log) _log.Add(s); });
        _app = DaemonHost.Build(new DaemonOptions { Host = "127.0.0.1", Port = 0 }, _profiles.Registry(), _manager, s => { lock (_log) _log.Add(s); });
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()), Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task DisposeAsync()
    {
        _launcher.ReleaseStream.TrySetResult();
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _profiles.Dispose();
    }

    static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Stats_Endpoint_Reports_Nothing_Loaded_Then_Slot_And_Recent()
    {
        var idle = await _http.GetFromJsonAsync<StatsSnapshot>("/veron/stats", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Null(idle!.Model);

        _launcher.SlotsJson = "[{\"id\":0,\"n_ctx\":8192,\"is_processing\":false,\"n_prompt_tokens\":5,\"n_prompt_tokens_processed\":5,\"n_prompt_tokens_cache\":0,\"next_token\":[{\"n_decoded\":3}]}]";
        _launcher.LogText =
            "0.00.100.000 I slot print_timing: id  0 | task 1 | prompt eval time =     100.00 ms /    35 tokens (    2.86 ms per token,   350.00 tokens per second)\n" +
            "0.00.100.001 I slot print_timing: id  0 | task 1 |        eval time =    1000.00 ms /   512 tokens (    1.95 ms per token,   512.00 tokens per second)\n" +
            "0.00.100.002 I slot print_timing: id  0 | task 1 | draft acceptance = 0.50000 (  1 accepted /   2 generated), mean len =  2.90\n";
        using var resp = await _http.PostAsync("/v1/chat/completions", Json(new { model = "alpha", messages = Array.Empty<object>() }));
        _launcher.ReleaseStream.TrySetResult();
        await resp.Content.ReadAsStringAsync();

        var s = await _http.GetFromJsonAsync<StatsSnapshot>("/veron/stats", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("alpha", s!.Model);
        Assert.Equal("alpha.gguf", s.From);
        Assert.Equal(new SlotSnapshot(false, 5, 5, 0, 3, 8192), s.Slot);
        var r = Assert.Single(s.Recent);
        Assert.Equal(35, r.PromptTokens);
        Assert.Equal(512, r.OutputTokens);
        Assert.Equal(0.5, r.DraftAcceptance!.Value, 3);
    }

    [Fact]
    public async Task DaemonClient_Stats_Deserializes_Snapshot()
    {
        var uri = new Uri(_app.Urls.First());
        var snap = await Task.Run(() => new DaemonClient(uri.Host, uri.Port).Stats());
        Assert.Null(snap.Model);
        Assert.Empty(snap.Recent);
    }

    [Fact]
    public async Task Health_Identifies_Veron()
    {
        var doc = await _http.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("veron", doc.GetProperty("service").GetString());
    }

    [Fact]
    public async Task Models_Endpoint_Lists_Profiles_Without_Loading()
    {
        var doc = await _http.GetFromJsonAsync<JsonElement>("/v1/models");
        var ids = doc.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
        Assert.Equal(new[] { "alpha", "beta" }, ids);
        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public async Task Request_Loads_Model_And_Streams_Chunks_As_They_Arrive()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions?x=1")
        { Content = Json(new { model = "alpha", stream = true }) };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-test");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType!.MediaType);
        Assert.Equal("alpha", resp.Headers.GetValues("X-Upstream-Model").Single());

        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        // the first two events arrive while upstream is still holding the stream open
        Assert.Equal("data: {\"n\":1}", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        await reader.ReadLineAsync();
        Assert.Equal("data: {\"n\":2}", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        _launcher.ReleaseStream.SetResult();
        string rest = await reader.ReadToEndAsync();
        Assert.Contains("[DONE]", rest);

        var seen = _launcher.Requests.Single();
        Assert.Equal("/v1/chat/completions?x=1", seen.Path);
        Assert.Equal("{\"model\":\"alpha\",\"stream\":true}", seen.Body); // body forwarded byte for byte
        Assert.Equal("Bearer sk-test", seen.Auth);
        Assert.Equal(new[] { "alpha" }, _launcher.Started);
    }

    [Fact]
    public async Task Anthropic_Messages_Pass_Through_With_Status_And_Headers()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages") { Content = Json(new { model = "Alpha-27B" }) };
        req.Headers.Add("x-api-key", "abc");
        using var resp = await _http.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alpha", doc.GetProperty("served_by").GetString()); // resolved via alias
        Assert.Equal("abc", _launcher.Requests.Single().Auth);
    }

    [Fact]
    public async Task Request_For_Other_Model_Swaps()
    {
        (await _http.PostAsync("/v1/messages", Json(new { model = "alpha" }))).EnsureSuccessStatusCode();
        (await _http.PostAsync("/v1/messages", Json(new { model = "beta" }))).EnsureSuccessStatusCode();

        Assert.Equal(new[] { "alpha", "beta" }, _launcher.Started);
        Assert.Equal("beta", _manager.Status().Single().Name);
    }

    [Fact]
    public async Task Unknown_Model_Goes_To_Loaded_Model_Otherwise_404()
    {
        var notFound = await _http.PostAsync("/v1/messages", Json(new { model = "claude-haiku-4-5" }));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        var err = await notFound.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_found_error", err.GetProperty("error").GetProperty("type").GetString());

        (await _http.PostAsync("/v1/messages", Json(new { model = "alpha" }))).EnsureSuccessStatusCode();
        var routed = await _http.PostAsync("/v1/messages", Json(new { model = "claude-haiku-4-5" }));
        Assert.Equal(HttpStatusCode.Created, routed.StatusCode);
        Assert.Equal(new[] { "alpha" }, _launcher.Started);
        lock (_log) Assert.Contains(_log, l => l.Contains("unknown model 'claude-haiku-4-5'"));
    }

    [Fact]
    public async Task Broken_Profile_Is_Reported_Not_Routed_To_Loaded_Model()
    {
        File.WriteAllText(Path.Join(_profiles.ModelfilesDir, "broken"), "FROM missing.gguf\n");
        (await _http.PostAsync("/v1/messages", Json(new { model = "alpha" }))).EnsureSuccessStatusCode();

        var resp = await _http.PostAsync("/v1/messages", Json(new { model = "broken" }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var msg = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("Profile 'broken' cannot be served", msg);
        Assert.Contains("missing.gguf", msg);
    }

    [Fact]
    public async Task Missing_Model_Field_Without_Loaded_Model_Is_400()
    {
        var resp = await _http.PostAsync("/v1/messages", Json(new { messages = Array.Empty<object>() }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Failed_Load_Returns_503_With_Message()
    {
        _launcher.FailStartup = true;
        var resp = await _http.PostAsync("/v1/messages", Json(new { model = "alpha" }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Failed to load model 'alpha'", err.GetProperty("error").GetProperty("message").GetString());
        Assert.Empty(_manager.Status());
    }

    [Fact]
    public async Task Client_Disconnect_Cancels_Upstream_Request()
    {
        (await _http.PostAsync("/veron/load", Json(new { model = "alpha" }))).EnsureSuccessStatusCode();

        var resp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/slow") { Content = Json(new { model = "alpha" }) },
                                         HttpCompletionOption.ResponseHeadersRead);
        var stream = await resp.Content.ReadAsStreamAsync();
        await new StreamReader(stream).ReadLineAsync(); // got the first chunk
        resp.Dispose();                                 // client goes away

        await _launcher.SlowRequestAborted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // the lease is released once the proxy notices
        for (int i = 0; i < 100 && _manager.Status().Single().ActiveRequests > 0; i++) await Task.Delay(20);
        Assert.Equal(0, _manager.Status().Single().ActiveRequests);
    }

    [Fact]
    public async Task Admin_Load_Ps_Unload()
    {
        (await _http.PostAsync("/veron/load", Json(new { model = "beta" }))).EnsureSuccessStatusCode();

        var ps = await _http.GetFromJsonAsync<JsonElement>("/veron/ps");
        Assert.Equal("beta", ps[0].GetProperty("name").GetString());
        Assert.Equal("beta.gguf", ps[0].GetProperty("from").GetString());

        var un = await (await _http.PostAsync("/veron/unload", Json(new { model = "beta" }))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("beta", un.GetProperty("unloaded")[0].GetString());
        Assert.Equal(0, (await _http.GetFromJsonAsync<JsonElement>("/veron/ps")).GetArrayLength());

        var missing = await _http.PostAsync("/veron/load", Json(new { model = "nope" }));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Sessions_Load_And_Unload_On_Close()
    {
        var open = await (await _http.PostAsync("/veron/sessions", Json(new { model = "alpha", pid = Environment.ProcessId })))
            .Content.ReadFromJsonAsync<JsonElement>();
        string id = open.GetProperty("id").GetString()!;
        Assert.False(open.GetProperty("wasAlreadyLoaded").GetBoolean());
        Assert.Single(_manager.Status());

        var close = await (await _http.DeleteAsync("/veron/sessions/" + id)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(close.GetProperty("unloaded").GetBoolean());
        Assert.Empty(_manager.Status());
    }

    [Fact]
    public async Task Client_Talks_To_Daemon_Admin_Api()
    {
        var uri = new Uri(_app.Urls.First());
        var client = new DaemonClient(uri.Host, uri.Port);

        Assert.True(client.IsRunning());
        client.Load("alpha");
        Assert.Equal("alpha", client.Ps().Single().Name);
        Assert.Equal(new[] { "alpha" }, client.Unload(null));

        string id = client.OpenSession("beta", Environment.ProcessId);
        Assert.True(client.CloseSession(id).Unloaded);

        var ex = Assert.Throws<DaemonException>(() => client.Load("nope"));
        Assert.Contains("not found", ex.Message);
        await Task.CompletedTask;
    }
}
