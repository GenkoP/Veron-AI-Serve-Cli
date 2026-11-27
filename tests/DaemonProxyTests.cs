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
    public string? PropsJson { get; set; }   // null → /props answers 500

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

        app.MapGet("/props", () => PropsJson is null ? Results.StatusCode(500) : Results.Text(PropsJson, "application/json"));

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

        // plain JSON echo endpoints (effort mapping tests)
        foreach (var path in new[] { "/v1/messages/count_tokens", "/v1/responses", "/chat/completions" })
            app.MapPost(path, async (HttpContext ctx) =>
            {
                string body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                Requests.Enqueue((model.Name, ctx.Request.Path + ctx.Request.QueryString, body, null));
                return Results.Json(new { served_by = model.Name });
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
        Assert.Equal(Program.Version, doc.GetProperty("version").GetString());
        var uri = new Uri(_app.Urls.First());
        Assert.Equal(Program.Version, await Task.Run(() => new DaemonClient(uri.Host, uri.Port).DaemonVersion()));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Unknown_Veron_Path_Is_404_And_Never_Reaches_Upstream(string method)
    {
        await _manager.AcquireAsync(_profiles.Registry().Resolve("alpha")!, default).ContinueWith(t => t.Result.Dispose());
        using var resp = await _http.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/veron/xyz"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("not_found", doc.RootElement.GetProperty("error").GetProperty("type").GetString());
        Assert.Empty(_launcher.Requests);
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
    public async Task Anthropic_Messages_Mid_Conversation_System_Is_Folded_Before_Upstream()
    {
        var resp = await _http.PostAsync("/v1/messages", Json(new
        {
            model = "alpha",
            system = "top",
            messages = new object[]
            {
                new { role = "user", content = "hello" },
                new { role = "system", content = "# Environment\nlinux ü" },
            },
        }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var sent = JsonDocument.Parse(_launcher.Requests.Single().Body).RootElement; // parses → length was right
        Assert.Equal("top", sent.GetProperty("system").GetString());
        var msg = Assert.Single(sent.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", msg.GetProperty("role").GetString());
        Assert.Equal("<system-reminder>\n# Environment\nlinux ü\n</system-reminder>",
                     msg.GetProperty("content")[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Chat_Completions_System_Messages_Are_Not_Touched()
    {
        string json = "{\"model\":\"alpha\",\"messages\":[{\"role\":\"user\",\"content\":\"q\"},{\"role\":\"system\",\"content\":\"s\"}]}";
        using var resp = await _http.PostAsync("/v1/chat/completions", new StringContent(json, Encoding.UTF8, "application/json"),
                                               default);
        _launcher.ReleaseStream.TrySetResult();
        await resp.Content.ReadAsStringAsync();
        Assert.Equal(json, _launcher.Requests.Single().Body);
    }

    // ── Effort mapping ───────────────────────────────────────────────────

    const string Qwen38Props =
        "{\"chat_template_caps\":{\"reasoning_effort_levels\":[\"low\",\"medium\",\"xhigh\"],\"reasoning_effort_rejected\":[\"minimal\",\"high\",\"max\"]}}";

    async Task<string> SendUpstream(string path, string json)
    {
        _launcher.ReleaseStream.TrySetResult(); // the fake /v1/chat/completions streams until released
        using var resp = await _http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.True(resp.IsSuccessStatusCode, $"{path}: {(int)resp.StatusCode}");
        await resp.Content.ReadAsStringAsync();
        return _launcher.Requests.Last().Body;
    }

    [Theory]
    [InlineData("/v1/messages",              "{\"model\":\"alpha\",\"output_config\":{\"effort\":\"high\"}}",  "output_config.effort", "high", "xhigh")]
    [InlineData("/v1/messages/count_tokens", "{\"model\":\"alpha\",\"output_config\":{\"effort\":\"max\"}}",   "output_config.effort", "max", "xhigh")]
    [InlineData("/v1/chat/completions",      "{\"model\":\"alpha\",\"reasoning_effort\":\"high\"}",              "reasoning_effort",     "high", "xhigh")]
    [InlineData("/chat/completions",         "{\"model\":\"alpha\",\"reasoning_effort\":\"minimal\"}",           "reasoning_effort",     "minimal", "low")]
    [InlineData("/v1/responses",             "{\"model\":\"alpha\",\"input\":\"hi\",\"reasoning\":{\"effort\":\"high\"}}", "reasoning.effort", "high", "xhigh")]
    public async Task Rejected_Effort_Is_Mapped_Before_Upstream(string path, string json, string field, string from, string to)
    {
        _launcher.PropsJson = Qwen38Props;
        var sent = JsonDocument.Parse(await SendUpstream(path, json)).RootElement;

        var e = sent;
        foreach (var name in field.Split('.')) e = e.GetProperty(name);
        Assert.Equal(to, e.GetString());
        lock (_log)
        {
            Assert.Contains("alpha template effort levels: low, medium, xhigh (rejected: minimal, high, max)", _log);
            Assert.Contains($"effort \"{from}\" not supported by alpha's template, using \"{to}\"", _log);
        }
    }

    [Theory]
    [InlineData("/v1/messages",         "{\"model\":\"alpha\", \"output_config\":{\"effort\":\"xhigh\"}}")]
    [InlineData("/v1/messages",         "{\"model\":\"alpha\", \"output_config\":{\"effort\":\"turbo\"}}")]
    [InlineData("/v1/chat/completions", "{\"model\":\"alpha\", \"reasoning_effort\":\"medium\"}")]
    [InlineData("/v1/responses",        "{\"model\":\"alpha\", \"reasoning\":{\"effort\":\"low\"}}")]
    public async Task Supported_Or_Unknown_Effort_Is_Forwarded_Byte_For_Byte(string path, string json)
    {
        _launcher.PropsJson = Qwen38Props;
        Assert.Equal(json, await SendUpstream(path, json));
        lock (_log) Assert.DoesNotContain(_log, l => l.Contains("not supported by"));
    }

    [Fact]
    public async Task Without_Props_Info_Rejected_Looking_Effort_Is_Not_Touched()
    {
        _launcher.PropsJson = null; // /props fails → load still succeeds, nothing is rewritten
        string json = "{\"model\":\"alpha\", \"output_config\":{\"effort\":\"high\"}}";
        Assert.Equal(json, await SendUpstream("/v1/messages", json));
        lock (_log) Assert.Contains(_log, l => l.StartsWith("could not read chat template effort levels of alpha"));
    }

    [Fact]
    public async Task Effort_Rewrite_Is_Logged_Once_Per_Loaded_Model()
    {
        _launcher.PropsJson = Qwen38Props;
        string json = "{\"model\":\"alpha\",\"output_config\":{\"effort\":\"high\"}}";
        await SendUpstream("/v1/messages", json);
        await SendUpstream("/v1/messages", json);
        await SendUpstream("/v1/messages", json.Replace("high", "max"));

        lock (_log)
        {
            Assert.Single(_log, l => l.Contains("effort \"high\" not supported"));
            Assert.Single(_log, l => l.Contains("effort \"max\" not supported"));
        }
        Assert.All(_launcher.Requests, r => Assert.Contains("\"effort\":\"xhigh\"", r.Body));
    }

    [Fact]
    public async Task Fold_And_Effort_Mapping_Compose_On_Messages()
    {
        _launcher.PropsJson = Qwen38Props;
        string json = JsonSerializer.Serialize(new
        {
            model = "alpha",
            thinking = new { type = "adaptive" },
            output_config = new { effort = "high" },
            messages = new object[]
            {
                new { role = "user", content = "hello" },
                new { role = "system", content = "env ü" },
            },
        });
        var sent = JsonDocument.Parse(await SendUpstream("/v1/messages", json)).RootElement;

        Assert.Equal("xhigh", sent.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("adaptive", sent.GetProperty("thinking").GetProperty("type").GetString());
        var msg = Assert.Single(sent.GetProperty("messages").EnumerateArray());
        Assert.Equal("<system-reminder>\nenv ü\n</system-reminder>", msg.GetProperty("content")[1].GetProperty("text").GetString());
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
