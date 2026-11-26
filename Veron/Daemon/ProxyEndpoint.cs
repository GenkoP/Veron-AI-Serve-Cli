using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Veron.Daemon;

/// <summary>
/// Forwards any API request (OpenAI /v1/*, Anthropic /v1/messages, llama.cpp native) to the
/// llama-server serving the request's "model", loading or swapping it first.
/// The response is streamed back unchanged (SSE included). Request bodies are forwarded as-is,
/// except that mid-conversation system messages in /v1/messages are folded (see <see cref="SystemMessageFolder"/>).
/// </summary>
public sealed class ProxyEndpoint(IModelRegistry registry, ModelManager manager, HttpClient http, Action<string> log)
{
    static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Connection", "Transfer-Encoding", "Upgrade", "TE", "Trailer",
        "Proxy-Authenticate", "Proxy-Authorization", "Host",
    };

    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect      = false,
        UseProxy               = false,
        UseCookies             = false,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    public async Task HandleAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var ct  = ctx.RequestAborted;

        byte[] body;
        using (var ms = new MemoryStream())
        {
            await req.Body.CopyToAsync(ms, ct);
            body = ms.ToArray();
        }

        string? requested = ExtractModel(body);
        var model = requested is null ? null : registry.Resolve(requested);

        if (model is null && requested is not null && registry.ProfileError(requested) is { } profileError)
        {
            await WriteErrorAsync(ctx, 404, "not_found_error", profileError);
            return;
        }

        if (model is null)
        {
            // Fallback: unknown / missing name goes to the loaded model (e.g. Claude Code's Haiku requests)
            if (manager.CurrentModelName is { } current && registry.Resolve(current) is { } loaded)
            {
                if (requested is not null) log($"unknown model '{requested}' — routing to loaded model {current}");
                model = loaded;
            }
            else if (requested is null)
            {
                await WriteErrorAsync(ctx, 400, "invalid_request_error",
                    "Request has no \"model\" field and no model is loaded");
                return;
            }
            else
            {
                await WriteErrorAsync(ctx, 404, "not_found_error",
                    $"Model '{requested}' not found. Available profiles: veron ls");
                return;
            }
        }

        ModelManager.Lease lease;
        try { lease = await manager.AcquireAsync(model, ct); }
        catch (ModelLoadException ex) { await WriteErrorAsync(ctx, 503, "api_error", ex.Message); return; }
        catch (OperationCanceledException) { return; } // client went away while waiting

        if (HttpMethods.IsPost(req.Method) && req.Path.StartsWithSegments("/v1/messages") &&
            SystemMessageFolder.Fold(body, out _) is { } rewritten)
            body = rewritten;

        using (lease)
        {
            using var up = new HttpRequestMessage(new HttpMethod(req.Method), lease.BaseUrl + req.Path + req.QueryString);
            if (body.Length > 0) up.Content = new ByteArrayContent(body);

            foreach (var (key, values) in req.Headers)
            {
                if (HopByHop.Contains(key)) continue;
                if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue; // body may be rewritten; ByteArrayContent sets it
                if (key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    up.Content?.Headers.TryAddWithoutValidation(key, (IEnumerable<string?>)values);
                else
                    up.Headers.TryAddWithoutValidation(key, (IEnumerable<string?>)values);
            }

            HttpResponseMessage resp;
            try { resp = await http.SendAsync(up, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (HttpRequestException ex)
            {
                await WriteErrorAsync(ctx, 502, "api_error", $"llama-server for '{model.Name}' is unreachable: {ex.Message}");
                return;
            }

            using (resp)
            {
                ctx.Response.StatusCode = (int)resp.StatusCode;
                foreach (var h in resp.Headers)
                    if (!HopByHop.Contains(h.Key)) ctx.Response.Headers[h.Key] = h.Value.ToArray();
                foreach (var h in resp.Content.Headers)
                    if (!HopByHop.Contains(h.Key)) ctx.Response.Headers[h.Key] = h.Value.ToArray();

                ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

                try
                {
                    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    var buffer = new byte[16 * 1024];
                    int n;
                    while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, n), ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                }
                catch (OperationCanceledException) { } // client disconnected → upstream request is cancelled
                catch (IOException) { }                // upstream died mid-stream
                catch (HttpRequestException) { }
            }
        }
    }

    /// <summary>Reads the top-level "model" string from a JSON body, if any.</summary>
    public static string? ExtractModel(byte[] body)
    {
        if (body.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("model", out var m) &&
                   m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } s
                ? s : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Error body readable by both OpenAI and Anthropic clients.</summary>
    public static Task WriteErrorAsync(HttpContext ctx, int status, string type, string message)
    {
        if (ctx.Response.HasStarted) return Task.CompletedTask;
        ctx.Response.StatusCode  = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type  = "error",
            error = new { type, message },
        }));
    }
}
