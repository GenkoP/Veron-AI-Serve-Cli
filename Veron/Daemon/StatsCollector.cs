using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Veron.Daemon;

/// <summary>One finished request, parsed from llama-server's print_timing log lines.</summary>
public sealed record RequestTiming(
    int Task, DateTimeOffset FinishedAt,
    int PromptTokens, double PromptTokensPerSecond,
    int OutputTokens, double OutputTokensPerSecond,
    double? DraftAcceptance);

/// <summary>Live state of the llama-server slot, from GET /slots.</summary>
public sealed record SlotSnapshot(
    bool IsProcessing, int PromptTokens, int PromptTokensProcessed, int PromptTokensCached, int Decoded, int NCtx);

/// <summary>What `GET /veron/stats` returns. <c>Model == null</c> means nothing is loaded.</summary>
public sealed record StatsSnapshot(
    DateTimeOffset Now, string? Model, string? From, int Context, int? Pid, int ActiveRequests,
    SlotSnapshot? Slot, IReadOnlyList<RequestTiming> Recent);

/// <summary>Incremental parse state: unfinished prompt lines and the requests completed so far (oldest first).</summary>
public sealed class TimingLogState
{
    internal const int MaxKept = 50;
    internal readonly Dictionary<int, (int Tokens, double Tps)> Prompts = new();
    public List<RequestTiming> Requests { get; } = new();
}

public static class TimingLogParser
{
    const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    static readonly Regex Line = new(
        @"^(\d+)\.(\d+)\.(\d+)\.(\d+)\s+\w\s+slot print_timing:\s+id\s+\d+\s+\|\s+task\s+(\d+)\s+\|\s*(.*)$", Opts);
    static readonly Regex PromptEval = new(
        @"^prompt eval time\s*=\s*[\d.]+ ms\s*/\s*(\d+) tokens\s*\(.*?,\s*([\d.]+) tokens per second\)", Opts);
    static readonly Regex Eval = new(
        @"^eval time\s*=\s*[\d.]+ ms\s*/\s*(\d+) tokens\s*\(.*?,\s*([\d.]+) tokens per second\)", Opts);
    static readonly Regex Draft = new(@"^draft acceptance\s*=\s*([\d.]+)", Opts);

    /// <summary>Parses a whole log (or chunk) with a fresh state. <paramref name="processStart"/> anchors the line prefixes.</summary>
    public static IReadOnlyList<RequestTiming> Parse(IEnumerable<string> lines, DateTimeOffset processStart)
    {
        var state = new TimingLogState();
        Parse(lines, state, offset => processStart + offset);
        return state.Requests;
    }

    /// <summary>
    /// Feeds lines into <paramref name="state"/>. A request completes at its "eval time" line; a "draft acceptance"
    /// line (possibly in a later call) updates the already completed request. Bad lines are ignored.
    /// </summary>
    public static void Parse(IEnumerable<string> lines, TimingLogState state, Func<TimeSpan, DateTimeOffset> finishedAt)
    {
        foreach (var raw in lines)
        {
            try { ParseLine(raw, state, finishedAt); }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException) { }
        }
    }

    static void ParseLine(string raw, TimingLogState state, Func<TimeSpan, DateTimeOffset> finishedAt)
    {
        if (!raw.Contains("print_timing", StringComparison.Ordinal)) return;
        var m = Line.Match(raw.TrimEnd());
        if (!m.Success) return;

        int task = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
        string rest = m.Groups[6].Value.TrimStart();

        if (PromptEval.Match(rest) is { Success: true } p)
        {
            state.Prompts[task] = (Int(p, 1), Dbl(p, 2));
        }
        else if (Eval.Match(rest) is { Success: true } e)
        {
            var prefix = new TimeSpan(
                long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * TimeSpan.TicksPerMinute +
                long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond +
                long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) * TimeSpan.TicksPerMillisecond +
                long.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * (TimeSpan.TicksPerMillisecond / 1000));
            state.Prompts.Remove(task, out var prompt);
            state.Requests.Add(new RequestTiming(task, finishedAt(prefix), prompt.Tokens, prompt.Tps,
                                                 Int(e, 1), Dbl(e, 2), null));
            if (state.Requests.Count > TimingLogState.MaxKept)
                state.Requests.RemoveRange(0, state.Requests.Count - TimingLogState.MaxKept);
        }
        else if (Draft.Match(rest) is { Success: true } d)
        {
            int i = state.Requests.FindLastIndex(r => r.Task == task);
            if (i >= 0) state.Requests[i] = state.Requests[i] with { DraftAcceptance = Dbl(d, 1) };
        }
    }

    static int Int(Match m, int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
    static double Dbl(Match m, int g) => double.Parse(m.Groups[g].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>
/// Builds <see cref="StatsSnapshot"/>s: ModelManager status + recent timings (read incrementally from the
/// model log) + live slot state (llama-server GET /slots). Thread-safe.
/// </summary>
public sealed class StatsCollector : IDisposable
{
    public const int RecentCount = 10;
    static readonly TimeSpan SlotsTimeout = TimeSpan.FromSeconds(2);

    readonly ModelManager _manager;
    readonly HttpClient _http;
    readonly TimeProvider _time;
    readonly Func<int, DateTimeOffset?> _processStart;
    readonly SemaphoreSlim _lock = new(1, 1);

    // log tailing state for the currently loaded model
    string? _logPath;
    int _logPid;
    long _offset;
    string _partial = "";
    TimingLogState _state = new();
    DateTimeOffset? _start;

    public StatsCollector(ModelManager manager, HttpMessageHandler? handler = null,
                          TimeProvider? time = null, Func<int, DateTimeOffset?>? processStart = null)
    {
        _manager      = manager;
        _http         = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _time         = time ?? TimeProvider.System;
        _processStart = processStart ?? DefaultProcessStart;
    }

    static DateTimeOffset? DefaultProcessStart(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return new DateTimeOffset(p.StartTime); }
        catch { return null; }
    }

    public async Task<StatsSnapshot> GetAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        if (_manager.Status().FirstOrDefault() is not { } st)
        {
            await _lock.WaitAsync(ct);
            try { Reset(null, 0); } finally { _lock.Release(); }
            return new StatsSnapshot(now, null, null, 0, null, 0, null, Array.Empty<RequestTiming>());
        }

        RequestTiming[] recent;
        await _lock.WaitAsync(ct);
        try
        {
            UpdateLog(st);
            recent = _state.Requests.AsEnumerable().Reverse().Take(RecentCount).ToArray();
        }
        finally { _lock.Release(); }

        var slot = await FetchSlotAsync(st.Port, ct);
        return new StatsSnapshot(now, st.Name, st.From, st.Context, st.Pid, st.ActiveRequests, slot, recent);
    }

    void Reset(string? path, int pid)
    {
        _logPath = path; _logPid = pid; _offset = 0; _partial = "";
        _state = new TimingLogState();
        _start = null;
    }

    void UpdateLog(ModelStatus st)
    {
        if (_logPath != st.LogPath || _logPid != st.Pid) Reset(st.LogPath, st.Pid);
        try
        {
            using var fs = new FileStream(st.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _offset) Reset(st.LogPath, st.Pid);
            if (fs.Length == _offset) return;

            fs.Seek(_offset, SeekOrigin.Begin);
            var buf = new byte[fs.Length - _offset];
            int n = fs.Read(buf, 0, buf.Length);
            _offset += n;

            string text = _partial + Encoding.UTF8.GetString(buf, 0, n);
            int nl = text.LastIndexOf('\n');
            if (nl < 0) { _partial = text; return; }
            _partial = text[(nl + 1)..];

            var lines = text[..nl].Split('\n');
            TimingLogParser.Parse(lines, _state, FinishedAt);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        DateTimeOffset FinishedAt(TimeSpan offset)
        {
            _start ??= _processStart(st.Pid);
            return _start is { } s ? s + offset : _time.GetUtcNow();
        }
    }

    async Task<SlotSnapshot?> FetchSlotAsync(int port, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(SlotsTimeout);
            using var resp = await _http.GetAsync($"http://127.0.0.1:{port}/slots", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            JsonElement? pick = null;
            foreach (var s in doc.RootElement.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                pick ??= s;
                if (GetBool(s, "is_processing")) { pick = s; break; }
            }
            if (pick is not { } slot) return null;

            int decoded = 0;
            if (slot.TryGetProperty("next_token", out var nt) && nt.ValueKind == JsonValueKind.Array &&
                nt.GetArrayLength() > 0 && nt[0].ValueKind == JsonValueKind.Object)
                decoded = GetInt(nt[0], "n_decoded");

            return new SlotSnapshot(GetBool(slot, "is_processing"), GetInt(slot, "n_prompt_tokens"),
                                    GetInt(slot, "n_prompt_tokens_processed"), GetInt(slot, "n_prompt_tokens_cache"),
                                    decoded, GetInt(slot, "n_ctx"));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    static int GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;

    public void Dispose() { _http.Dispose(); _lock.Dispose(); }
}
