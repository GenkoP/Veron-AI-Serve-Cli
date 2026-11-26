using Veron;
using Veron.Daemon;
using Xunit;

namespace Veron.Tests;

public class StatsCommandTests
{
    static DateTimeOffset Local(int h, int m, int s) =>
        new(new DateTime(2026, 11, 26, h, m, s, DateTimeKind.Local));

    static readonly RequestTiming[] Recent =
    {
        new(2, Local(17, 41, 52), 35, 106.11, 512, 92.02, 0.63258),
        new(1, Local(17, 40, 10), 20024, 1113.4, 256, 84.4, null),
    };

    static StatsSnapshot Snap(SlotSnapshot? slot, params RequestTiming[] recent) =>
        new(Local(17, 42, 5), "qwen38", "qwen.gguf", 262144, 123, 0, slot, recent);

    static string[] Lines(string s) => s.Replace("\r", "").Split('\n');

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1.1.0", true)]
    [InlineData("1.2.0", false)]
    [InlineData("1.3.0", false)]
    public void IsDaemonOlder(string? daemon, bool older) =>
        Assert.Equal(older, CmdStats.IsDaemonOlder(daemon, "1.2.0"));

    [Fact]
    public void Render_Idle()
    {
        var snap = Snap(new SlotSnapshot(false, 671, 0, 0, 0, 262144), Recent);
        var text = CmdStats.Render(snap, null, 100, watch: true);
        var l = Lines(text);
        Assert.StartsWith("Veron stats · qwen38 · ctx 262,144", l[0]);
        Assert.EndsWith("17:42:05  (Ctrl+C to quit)", l[0]);
        Assert.Contains("State      ○ idle", text);
        Assert.Contains("Prompt     —", text);
        Assert.Contains("Context    671 / 262,144  (0.3%)", text);
        Assert.Contains("Output     —", text);
        Assert.DoesNotContain("671 tokens", text);
        Assert.Contains("Recent requests", text);
        Assert.Contains("17:41:52", text);
        Assert.Contains("63%", text);
        Assert.Contains("20,024", text);
        Assert.Contains("1,113", text);
        Assert.Contains("92.0", text);
    }

    [Fact]
    public void Render_NonWatch_HasNoQuitHint()
    {
        var text = CmdStats.Render(Snap(null), null, 100, watch: false);
        Assert.DoesNotContain("Ctrl+C", text);
        Assert.Contains("17:42:05", text);
    }

    [Fact]
    public void Render_PromptProcessing_Shows_Tokens_So_Far_And_Rate()
    {
        var slot = new SlotSnapshot(true, 10282, 8234, 0, 0, 262144);
        var text = CmdStats.Render(Snap(slot), 1050.4, 100, watch: true);
        Assert.Contains("State      ● processing prompt  10,282 tokens so far  1,050 t/s", text);
        Assert.Contains("Output     —", text);
    }

    [Fact]
    public void Render_PromptProcessing_Without_Rate()
    {
        var slot = new SlotSnapshot(true, 2090, 42, 0, 0, 262144);
        Assert.Contains("State      ● processing prompt  2,090 tokens so far\n",
            CmdStats.Render(Snap(slot), null, 100, true) + "\n");
    }

    [Fact]
    public void Render_Generating_Uses_Decoded_For_Phase_And_Output()
    {
        // n_prompt (10946) already includes the 39 generated tokens
        var slot = new SlotSnapshot(true, 10946, 10908, 0, 39, 262144);
        var text = CmdStats.Render(Snap(slot, Recent), 91.3, 100, watch: true);
        Assert.Contains("State      ● generating", text);
        Assert.DoesNotContain("processing prompt", text);
        Assert.Contains("Prompt     10,907 tokens", text);
        Assert.DoesNotContain("from cache", text);
        Assert.Contains("Context    10,946 / 262,144  (4.2%)", text);
        Assert.Contains("Output     39 tokens    91.3 t/s live", text);
    }

    [Fact]
    public void Render_Generating_Shows_Cache_When_Present()
    {
        var slot = new SlotSnapshot(true, 20410, 224, 19800, 386, 262144);
        var text = CmdStats.Render(Snap(slot), 91.3, 100, true);
        Assert.Contains("Prompt     20,024 tokens  (19,800 from cache)", text);
        Assert.Contains("Context    20,410 / 262,144  (7.8%)", text);
    }

    [Fact]
    public void Render_Context_Falls_Back_To_Snapshot_When_NCtx_Zero()
    {
        var slot = new SlotSnapshot(false, 10, 10, 0, 0, 0);
        Assert.Contains("Context    10 / 262,144", CmdStats.Render(Snap(slot), null, 100, true));
    }

    [Fact]
    public void Render_NoModel()
    {
        var snap = new StatsSnapshot(Local(17, 42, 5), null, null, 0, null, 0, null, Array.Empty<RequestTiming>());
        Assert.Equal("No model loaded (Veron server at http://127.0.0.1:11435).",
            CmdStats.Render(snap, null, 100, true, "http://127.0.0.1:11435"));
    }

    [Fact]
    public void LiveRate_Computes_Over_Window()
    {
        var r = new LiveRate(TimeSpan.FromSeconds(2));
        var t = Local(12, 0, 0);
        Assert.Null(r.Add(t, 0));
        Assert.Equal(50.0, r.Add(t.AddSeconds(1), 50)!.Value, 6);
        Assert.Equal(60.0, r.Add(t.AddSeconds(2), 120)!.Value, 6);
        // t=0 sample falls out; window now t=1..3
        Assert.Equal(75.0, r.Add(t.AddSeconds(3), 200)!.Value, 6);
    }

    [Fact]
    public void LiveRate_Resets_When_Decoded_Drops()
    {
        var r = new LiveRate(TimeSpan.FromSeconds(2));
        var t = Local(12, 0, 0);
        r.Add(t, 100);
        r.Add(t.AddSeconds(1), 200);
        Assert.Null(r.Add(t.AddSeconds(1.5), 5));
        Assert.Equal(20.0, r.Add(t.AddSeconds(2.0), 15)!.Value, 6);
    }

    [Fact]
    public void LiveRate_Reset_Clears()
    {
        var r = new LiveRate(TimeSpan.FromSeconds(2));
        var t = Local(12, 0, 0);
        r.Add(t, 0); r.Add(t.AddSeconds(1), 10);
        r.Reset();
        Assert.Null(r.Add(t.AddSeconds(2), 20));
    }

    [Theory]
    [InlineData("--watch")]
    [InlineData("-w")]
    [InlineData("watch")]
    public void IsWatch_Recognises_All_Spellings(string arg) =>
        Assert.True(CmdStats.IsWatch(new[] { arg }));

    [Fact]
    public void IsWatch_False_Without_Flag() => Assert.False(CmdStats.IsWatch(Array.Empty<string>()));

    [Fact]
    public void FormatLine_With_And_Without_Draft()
    {
        Assert.Equal("17:41:52  qwen38  prompt 35 tok @ 106 t/s  output 512 tok @ 92.0 t/s  draft 63%",
            CmdStats.FormatLine("qwen38", Recent[0]));
        Assert.Equal("17:40:10  qwen38  prompt 20,024 tok @ 1,113 t/s  output 256 tok @ 84.4 t/s",
            CmdStats.FormatLine("qwen38", Recent[1]));
    }

    [Fact]
    public void NewTimings_Returns_Only_Unseen_Oldest_First()
    {
        var seen = new HashSet<(int?, int)> { (123, 1) };
        var snap = Snap(null, Recent);
        var fresh = CmdStats.TakeNew(snap, seen);
        Assert.Single(fresh);
        Assert.Equal(2, fresh[0].Task);
        Assert.Empty(CmdStats.TakeNew(snap, seen));
    }

    [Fact]
    public void TakeNew_After_Model_Change_Still_Reports_New_Requests()
    {
        var seen = new HashSet<(int?, int)>();
        var empty = new StatsSnapshot(Local(17, 0, 0), null, null, 0, null, 0, null, Array.Empty<RequestTiming>());
        Assert.Empty(CmdStats.TakeNew(empty, seen));          // first snapshot seeds (nothing to see)
        var loaded = Snap(null, Recent);                        // new model, requests already present
        Assert.Equal(2, CmdStats.TakeNew(loaded, seen).Count);  // must not be swallowed
    }
}
