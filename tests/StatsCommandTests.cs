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

    [Fact]
    public void Render_Idle()
    {
        var snap = Snap(new SlotSnapshot(false, 35, 35, 0, 512, 262144), Recent);
        var text = CmdStats.Render(snap, null, 100, watch: true);
        var l = Lines(text);
        Assert.StartsWith("Veron stats · qwen38 · ctx 262,144", l[0]);
        Assert.EndsWith("17:42:05  (Ctrl+C to quit)", l[0]);
        Assert.Contains("State      ○ idle", text);
        Assert.Contains("Context    547 / 262,144  (0.2%)", text);
        Assert.Contains("Output     512 tokens    —", text);
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
    public void Render_PromptProcessing()
    {
        var slot = new SlotSnapshot(true, 20024, 12288, 0, 0, 262144);
        var text = CmdStats.Render(Snap(slot), null, 100, watch: true);
        Assert.Contains("State      ● processing prompt 12,288 / 20,024 (61%)", text);
    }

    [Fact]
    public void Render_Generating()
    {
        var slot = new SlotSnapshot(true, 20024, 20024, 19800, 386, 262144);
        var text = CmdStats.Render(Snap(slot, Recent), 91.3, 100, watch: true);
        Assert.Contains("State      ● generating", text);
        Assert.Contains("Prompt     20,024 tokens  (19,800 from cache)", text);
        Assert.Contains("Context    20,410 / 262,144  (7.8%)", text);
        Assert.Contains("Output     386 tokens    91.3 t/s live", text);
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
}
