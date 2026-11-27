using System.Text;
using System.Text.Json.Nodes;
using Veron.Daemon;
using Xunit;

namespace Veron.Tests;

public class EffortMapperTests
{
    // Qwen3.8 fusion: /props → chat_template_caps
    static readonly EffortCaps Qwen38 = new(["low", "medium", "xhigh"], ["minimal", "high", "max"]);

    static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

    [Theory]
    [InlineData("high", "xhigh")]
    [InlineData("max", "xhigh")]     // nothing above → highest below
    [InlineData("minimal", "low")]
    public void Qwen38_Rejected_Levels_Map_To_Nearest_Supported(string level, string expected) =>
        Assert.Equal(expected, EffortMapper.Map(level, Qwen38));

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("xhigh")]
    [InlineData("none")]   // not rejected (llama-server handles "none" itself)
    [InlineData("HIGH")]   // not in the rejected list as spelled → llama-server does not reject it either
    public void Levels_That_Are_Not_Rejected_Are_Left_Alone(string level) =>
        Assert.Null(EffortMapper.Map(level, Qwen38));

    [Fact]
    public void Rejected_Level_Outside_Canonical_Order_Is_Left_Alone() =>
        Assert.Null(EffortMapper.Map("turbo", new EffortCaps(["low", "high"], ["turbo"])));

    [Fact]
    public void Empty_Caps_Never_Map()
    {
        Assert.Null(EffortMapper.Map("high", EffortCaps.None));
        Assert.Null(EffortMapper.Map("high", new EffortCaps([], ["high"]))); // rejected but nothing supported
    }

    [Fact]
    public void Supported_Levels_Outside_Canonical_Order_Are_Not_Targets()
    {
        Assert.Equal("medium", EffortMapper.Map("high", new EffortCaps(["medium", "turbo"], ["high"])));
        Assert.Null(EffortMapper.Map("high", new EffortCaps(["turbo"], ["high"])));
    }

    [Fact]
    public void Lowest_Level_Above_Wins_Regardless_Of_List_Order() =>
        Assert.Equal("high", EffortMapper.Map("medium", new EffortCaps(["max", "high", "low"], ["medium"])));

    // ── /props parsing ───────────────────────────────────────────────────

    [Fact]
    public void Parses_Props()
    {
        var caps = EffortCaps.Parse("""
            {"default_generation_settings":{},"chat_template_caps":{"supports_tools":true,
             "reasoning_effort_levels":["low","medium","xhigh"],"reasoning_effort_rejected":["minimal","high","max"]}}
            """);
        Assert.Equal(new[] { "low", "medium", "xhigh" }, caps.Levels);
        Assert.Equal(new[] { "minimal", "high", "max" }, caps.Rejected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"chat_template_caps\":null}")]
    [InlineData("{\"chat_template_caps\":{\"supports_tools\":true}}")] // older llama-server
    public void Props_Without_Effort_Info_Is_None(string json) => Assert.True(EffortCaps.Parse(json).IsEmpty);

    // ── Body rewriting ───────────────────────────────────────────────────

    static JsonNode Rewritten(string path, string json, string from, string to)
    {
        var result = EffortMapper.Rewrite(Body(json), path, Qwen38, out var rewrite);
        Assert.NotNull(result);
        Assert.Equal(new EffortRewrite(from, to), rewrite);
        return JsonNode.Parse(result!)!;
    }

    [Theory]
    [InlineData("/v1/messages")]
    [InlineData("/v1/messages/count_tokens")]
    public void Anthropic_Output_Config_Effort(string path)
    {
        var root = Rewritten(path, """
            {"model":"m","max_tokens":32000,"thinking":{"type":"adaptive"},"output_config":{"effort":"high","x":1},
             "messages":[{"role":"user","content":"ü"}]}
            """, "high", "xhigh");
        Assert.Equal("xhigh", (string?)root["output_config"]!["effort"]);
        Assert.Equal(1, (int)root["output_config"]!["x"]!);
        Assert.Equal("adaptive", (string?)root["thinking"]!["type"]);
        Assert.Equal(32000, (int)root["max_tokens"]!);
        Assert.Equal("ü", (string?)root["messages"]![0]!["content"]);
    }

    [Theory]
    [InlineData("/v1/chat/completions")]
    [InlineData("/chat/completions")]
    [InlineData("/v1/chat/completions/input_tokens")]
    [InlineData("/apply-template")]
    public void Chat_Completions_Reasoning_Effort(string path)
    {
        var root = Rewritten(path, """{"model":"m","reasoning_effort":"minimal","messages":[]}""", "minimal", "low");
        Assert.Equal("low", (string?)root["reasoning_effort"]);
    }

    [Theory]
    [InlineData("/v1/responses")]
    [InlineData("/responses")]
    [InlineData("/v1/responses/input_tokens")]
    public void Responses_Reasoning_Effort(string path)
    {
        var root = Rewritten(path, """{"model":"m","input":"hi","reasoning":{"effort":"max","summary":"auto"}}""", "max", "xhigh");
        Assert.Equal("xhigh", (string?)root["reasoning"]!["effort"]);
        Assert.Equal("auto", (string?)root["reasoning"]!["summary"]);
    }

    [Theory]
    [InlineData("/v1/messages", """{"model":"m","output_config":{"effort":"xhigh"}}""")]      // supported
    [InlineData("/v1/messages", """{"model":"m","output_config":{}}""")]
    [InlineData("/v1/messages", """{"model":"m","output_config":{"effort":3}}""")]
    [InlineData("/v1/messages", """{"model":"m","reasoning_effort":"high"}""")]               // not where Anthropic reads it
    [InlineData("/v1/chat/completions", """{"model":"m","output_config":{"effort":"high"}}""")] // ditto for OpenAI
    [InlineData("/v1/chat/completions", """{"model":"m","reasoning_effort":"medium"}""")]
    [InlineData("/v1/chat/completions", """{"model":"m","reasoning_effort":null}""")]
    [InlineData("/v1/responses", """{"model":"m","reasoning":"high"}""")]
    [InlineData("/v1/completions", """{"model":"m","reasoning_effort":"high"}""")]            // not a chat endpoint
    [InlineData("/v1/messages", "not json")]
    [InlineData("/v1/messages", "")]
    public void Nothing_To_Rewrite_Returns_Null(string path, string json)
    {
        Assert.Null(EffortMapper.Rewrite(Body(json), path, Qwen38, out var rewrite));
        Assert.Null(rewrite);
    }

    [Fact]
    public void No_Caps_Means_No_Rewrite() =>
        Assert.Null(EffortMapper.Rewrite(Body("""{"output_config":{"effort":"high"}}"""), "/v1/messages", EffortCaps.None, out _));
}
