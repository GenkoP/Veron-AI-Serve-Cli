using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Veron;
using Veron.Daemon;
using Xunit;

namespace Veron.Tests;

public class TimingLogParserTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    const string PromptLine = "0.24.466.535 I slot print_timing: id  0 | task 348 | prompt eval time =     329.84 ms /    35 tokens (    9.42 ms per token,   106.11 tokens per second)";
    const string EvalLine   = "0.24.466.537 I slot print_timing: id  0 | task 348 |        eval time =    5552.89 ms /   512 tokens (   10.87 ms per token,    92.02 tokens per second)";
    const string TotalLine  = "0.24.466.538 I slot print_timing: id  0 | task 348 |       total time =    5882.73 ms /   547 tokens";
    const string DraftLine  = "0.24.466.541 I slot print_timing: id  0 | task 348 | draft acceptance = 0.63258 (  334 accepted /   528 generated), mean len =  2.90";

    [Fact]
    public void Parses_Full_Request_With_Draft()
    {
        var r = Assert.Single(TimingLogParser.Parse(new[] { PromptLine, EvalLine, TotalLine, DraftLine }, Start));
        Assert.Equal(348, r.Task);
        Assert.Equal(35, r.PromptTokens);
        Assert.Equal(106.11, r.PromptTokensPerSecond, 2);
        Assert.Equal(512, r.OutputTokens);
        Assert.Equal(92.02, r.OutputTokensPerSecond, 2);
        Assert.Equal(0.63258, r.DraftAcceptance!.Value, 5);
        Assert.Equal(Start + TimeSpan.FromSeconds(24) + TimeSpan.FromMilliseconds(466) + TimeSpan.FromTicks(5370), r.FinishedAt);
    }

    [Fact]
    public void Parses_Request_Without_Draft_Line()
    {
        var r = Assert.Single(TimingLogParser.Parse(new[] { PromptLine, EvalLine, TotalLine }, Start));
        Assert.Null(r.DraftAcceptance);
    }

    [Fact]
    public void Minutes_Above_59_Are_Supported()
    {
        string line = "125.03.001.000 I slot print_timing: id  0 | task 7 |        eval time =    100.00 ms /    10 tokens (   10.00 ms per token,   100.00 tokens per second)";
        var r = Assert.Single(TimingLogParser.Parse(new[] { line }, Start));
        Assert.Equal(Start + TimeSpan.FromMinutes(125) + TimeSpan.FromSeconds(3) + TimeSpan.FromMilliseconds(1), r.FinishedAt);
        Assert.Equal(0, r.PromptTokens);
    }

    [Fact]
    public void Malformed_And_Unrelated_Lines_Are_Ignored()
    {
        var lines = new[]
        {
            "", "garbage", "0.00.000.000 I slot print_timing: id  0 | task x | eval time = nope",
            "0.24.466.537 I slot print_timing: id  0 | task 9 |        eval time =    1.00 ms /    abc tokens (",
            "0.01.000.000 I slot print_timing: id  0 | task 3 | n_gen =    249, tg =  82.00 t/s, tg_3s =  82.32 t/s",
            "99999999999999.1.1.1 I slot print_timing: id  0 | task 4 |        eval time =    1.00 ms /    5 tokens (    1.00 ms per token,   100.00 tokens per second)",
            PromptLine, EvalLine,
        };
        var r = Assert.Single(TimingLogParser.Parse(lines, Start));
        Assert.Equal(348, r.Task);
    }

    [Fact]
    public void Incremental_Chunks_Update_Draft_Of_Emitted_Request()
    {
        var state = new TimingLogState();
        TimingLogParser.Parse(new[] { PromptLine, EvalLine }, state, o => Start + o);
        Assert.Null(Assert.Single(state.Requests).DraftAcceptance);

        TimingLogParser.Parse(new[] { TotalLine, DraftLine }, state, o => Start + o);
        var r = Assert.Single(state.Requests);
        Assert.Equal(0.63258, r.DraftAcceptance!.Value, 5);
    }

    [Fact]
    public void Real_Log_Sample_Parses_When_Present()
    {
        string path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".veron", "logs", "qwen38-fusion-q4.log");
        if (!File.Exists(path)) return;
        string[] lines;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            lines = sr.ReadToEnd().Split('\n');
        }
        catch (IOException) { return; }
        Assert.All(TimingLogParser.Parse(lines, Start), r => Assert.True(r.OutputTokens >= 0));
    }
}

public sealed class StatsCollectorTests : IDisposable
{
    readonly TempProfiles _profiles = new();
    readonly FakeLauncher _launcher = new();
    readonly ManualTime _time = new();
    readonly DateTimeOffset _procStart = new(2026, 11, 26, 11, 0, 0, TimeSpan.Zero);

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(fn(r));
        }
    }

    const string Slots = """
        [{"id":0,"n_ctx":4096,"is_processing":true,"n_prompt_tokens":100,"n_prompt_tokens_processed":100,
          "n_prompt_tokens_cache":40,"next_token":[{"n_decoded":17,"n_remain":-1,"has_next_token":true}]}]
        """;

    static string Eval(int task, int tokens, string ts = "0.00.001.000") =>
        $"{ts} I slot print_timing: id  0 | task {task} |        eval time =    1000.00 ms /   {tokens} tokens (   10.00 ms per token,    50.00 tokens per second)\n";

    static ResolvedModel Model(string name) =>
        new(name, "/tmp/" + name, new ModelConfig { ModelPath = "/tmp/" + name + ".gguf", Alias = name, Wait = 5 });

    async Task<(ModelManager, string LogPath)> Loaded(string name = "a")
    {
        var m = new ModelManager(_launcher, TimeSpan.FromMinutes(5), _profiles.LogsDir, _time, allocatePort: () => 41000);
        using (await m.AcquireAsync(Model(name), default)) { }
        Directory.CreateDirectory(_profiles.LogsDir);
        return (m, m.Status().Single().LogPath);
    }

    StatsCollector Collector(ModelManager m, StubHandler? h = null) =>
        new(m, h ?? new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), _time, _ => _procStart);

    [Fact]
    public async Task Nothing_Loaded_Gives_Null_Model()
    {
        using var m = new ModelManager(_launcher, TimeSpan.FromMinutes(5), _profiles.LogsDir, _time);
        using var c = Collector(m);
        var s = await c.GetAsync();
        Assert.Null(s.Model);
        Assert.Null(s.Slot);
        Assert.Empty(s.Recent);
    }

    [Fact]
    public async Task Reads_Slot_And_Model_Info()
    {
        var (m, _) = await Loaded();
        using var _m = m;
        var h = new StubHandler(r =>
        {
            Assert.Equal("http://127.0.0.1:41000/slots", r.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Slots) };
        });
        using var c = Collector(m, h);
        var s = await c.GetAsync();
        Assert.Equal("a", s.Model);
        Assert.Equal(new SlotSnapshot(true, 100, 100, 40, 17, 4096), s.Slot);
        Assert.Equal(_launcher.Last.Pid, s.Pid);
    }

    [Fact]
    public async Task Slots_Failure_Gives_Null_Slot_Without_Throwing()
    {
        var (m, _) = await Loaded();
        using var _m = m;
        var h = new StubHandler(_ => throw new HttpRequestException("down"));
        using var c = Collector(m, h);
        Assert.Null((await c.GetAsync()).Slot);
    }

    [Fact]
    public async Task Reads_Log_Incrementally_Newest_First_Max_10()
    {
        var (m, log) = await Loaded();
        using var _m = m;
        using var c = Collector(m);

        File.WriteAllText(log, Eval(1, 10) + Eval(2, 20) + "0.00.002.000 I slot print_timing: id  0 | task 3 |        eval time =  1.00 ms /   30 tokens (");
        var s = await c.GetAsync();
        Assert.Equal(new[] { 2, 1 }, s.Recent.Select(r => r.Task));
        Assert.Equal(_procStart + TimeSpan.FromMilliseconds(1), s.Recent[0].FinishedAt);

        // finish the partial line and add many more
        File.AppendAllText(log, " 50.00 ms per token, 20.00 tokens per second)\n");
        for (int i = 4; i < 20; i++) File.AppendAllText(log, Eval(i, i));
        s = await c.GetAsync();
        Assert.Equal(10, s.Recent.Count);
        Assert.Equal(19, s.Recent[0].Task);
        Assert.Equal(10, s.Recent[^1].Task);
    }

    [Fact]
    public async Task Recreated_Log_Resets()
    {
        var (m, log) = await Loaded();
        using var _m = m;
        using var c = Collector(m);

        File.WriteAllText(log, Eval(1, 10) + Eval(2, 20) + Eval(3, 30));
        Assert.Equal(3, (await c.GetAsync()).Recent.Count);

        File.WriteAllText(log, Eval(1, 99));   // truncated, shorter than before
        var s = await c.GetAsync();
        var r = Assert.Single(s.Recent);
        Assert.Equal(99, r.OutputTokens);
    }

    [Fact]
    public async Task Missing_Log_Is_Fine()
    {
        var (m, _) = await Loaded();
        using var _m = m;
        using var c = Collector(m);
        Assert.Empty((await c.GetAsync()).Recent);
    }

    public void Dispose() => _profiles.Dispose();
}
