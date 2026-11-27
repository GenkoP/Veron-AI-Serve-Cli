using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Veron.Daemon;

/// <summary>
/// The reasoning effort levels of the loaded model's chat template, as llama-server reports them in
/// <c>GET /props</c> → <c>chat_template_caps</c>. <see cref="Levels"/> are supported (lowest → highest),
/// <see cref="Rejected"/> are levels the template raises on, which llama-server answers with HTTP 400.
/// Empty lists mean "no info": nothing is ever rewritten.
/// </summary>
public sealed record EffortCaps(IReadOnlyList<string> Levels, IReadOnlyList<string> Rejected)
{
    public static readonly EffortCaps None = new(Array.Empty<string>(), Array.Empty<string>());

    public bool IsEmpty => Levels.Count == 0 && Rejected.Count == 0;

    static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>GETs llama-server's /props. Throws on HTTP/network errors; missing fields give <see cref="None"/>.</summary>
    public static async Task<EffortCaps> FetchAsync(int port, CancellationToken ct)
    {
        using var resp = await Http.GetAsync("http://127.0.0.1:" + port + "/props", ct);
        resp.EnsureSuccessStatusCode();
        return Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Reads <c>chat_template_caps.reasoning_effort_levels/_rejected</c> from a /props body.</summary>
    public static EffortCaps Parse(string propsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(propsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("chat_template_caps", out var caps) ||
                caps.ValueKind != JsonValueKind.Object)
                return None;
            return new EffortCaps(Strings(caps, "reasoning_effort_levels"), Strings(caps, "reasoning_effort_rejected"));
        }
        catch (JsonException) { return None; }
    }

    static string[] Strings(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray()
            : Array.Empty<string>();
}

/// <summary>A rewrite done by <see cref="EffortMapper.Rewrite"/>.</summary>
public sealed record EffortRewrite(string From, string To);

/// <summary>
/// llama-server rejects (HTTP 400) a reasoning effort the chat template raises on — e.g. Qwen3.8 supports
/// low/medium/xhigh and rejects minimal/high/max, while Claude Code sends <c>output_config.effort: "high"</c>
/// on every request. This maps such a level to the nearest supported one before forwarding.
/// </summary>
public static class EffortMapper
{
    /// <summary>Canonical order, lowest first.</summary>
    public static readonly IReadOnlyList<string> Order = ["none", "minimal", "low", "medium", "high", "xhigh", "max"];

    static readonly JsonSerializerOptions Out = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The level to send instead of <paramref name="level"/>, or null to leave it unchanged.
    /// Only rejected levels are mapped: to the lowest supported level ranked above, else the highest one below.
    /// A level outside <see cref="Order"/>, or caps without supported levels, are left alone.
    /// </summary>
    public static string? Map(string level, EffortCaps caps)
    {
        if (!caps.Rejected.Contains(level, StringComparer.Ordinal)) return null;
        int rank = Rank(level);
        if (rank < 0) return null;

        var ranked = caps.Levels.Select(l => (Level: l, Rank: Rank(l))).Where(x => x.Rank >= 0).ToList();
        var above  = ranked.Where(x => x.Rank > rank).OrderBy(x => x.Rank).Select(x => x.Level).FirstOrDefault();
        return above ?? ranked.Where(x => x.Rank < rank).OrderByDescending(x => x.Rank).Select(x => x.Level).FirstOrDefault();
    }

    static int Rank(string level)
    {
        for (int i = 0; i < Order.Count; i++) if (Order[i] == level) return i;
        return -1;
    }

    /// <summary>
    /// Where the request body carries its effort, by endpoint (as llama-server reads it):
    /// Anthropic <c>output_config.effort</c>, OpenAI Responses <c>reasoning.effort</c>,
    /// OpenAI Chat Completions top-level <c>reasoning_effort</c>. Null for other endpoints.
    /// </summary>
    public static string[]? EffortField(PathString path)
    {
        if (path.StartsWithSegments("/v1/messages"))                  // incl. /count_tokens
            return ["output_config", "effort"];
        if (path.StartsWithSegments("/v1/responses") || path.StartsWithSegments("/responses")) // incl. /input_tokens
            return ["reasoning", "effort"];
        if (path.StartsWithSegments("/v1/chat/completions") || path.StartsWithSegments("/chat/completions") ||
            path.StartsWithSegments("/apply-template"))                 // incl. /input_tokens
            return ["reasoning_effort"];
        return null;
    }

    /// <summary>
    /// Returns the body with a rejected effort replaced, or null when nothing needs to change
    /// (send the original bytes).
    /// </summary>
    public static byte[]? Rewrite(byte[] body, PathString path, EffortCaps caps, out EffortRewrite? rewrite)
    {
        rewrite = null;
        if (caps.Rejected.Count == 0 || body.Length == 0 || EffortField(path) is not { } field) return null;

        try
        {
            // cheap read-only look first; only a body that needs the rewrite is re-serialised
            string? current;
            using (var doc = JsonDocument.Parse(body))
            {
                var e = doc.RootElement;
                foreach (var name in field)
                    if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e)) return null;
                if (e.ValueKind != JsonValueKind.String) return null;
                current = e.GetString();
            }
            if (current is null || Map(current, caps) is not { } mapped) return null;

            if (JsonNode.Parse(body) is not JsonObject obj) return null;
            for (int i = 0; i < field.Length - 1; i++)
                if (obj[field[i]] is JsonObject child) obj = child; else return null;
            obj[field[^1]] = mapped;

            rewrite = new EffortRewrite(current, mapped);
            return JsonSerializer.SerializeToUtf8Bytes(obj.Root, Out);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null; // unparseable or odd JSON: forward untouched, llama-server reports it
        }
    }
}
