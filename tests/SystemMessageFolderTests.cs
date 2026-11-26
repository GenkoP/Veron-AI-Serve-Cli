using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Veron.Daemon;
using Xunit;

namespace Veron.Tests;

public class SystemMessageFolderTests
{
    static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

    static JsonArray Fold(string json, int expectedFolded)
    {
        var result = SystemMessageFolder.Fold(Body(json), out int folded);
        Assert.Equal(expectedFolded, folded);
        Assert.NotNull(result);
        return JsonNode.Parse(result!)!["messages"]!.AsArray();
    }

    static string Reminder(string text) => "<system-reminder>\n" + text + "\n</system-reminder>";

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"model\":\"m\"}")]
    [InlineData("{\"model\":\"m\",\"messages\":\"x\"}")]
    [InlineData("{\"model\":\"m\",\"system\":\"top-level is fine\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}")]
    public void Nothing_To_Fold_Returns_Null(string json)
    {
        Assert.Null(SystemMessageFolder.Fold(Body(json), out int folded));
        Assert.Equal(0, folded);
    }

    [Fact]
    public void System_After_User_Is_Appended_To_That_User_Turn()
    {
        var msgs = Fold("""
            {"model":"m","messages":[
              {"role":"user","content":"hello"},
              {"role":"system","content":"# Environment\nlinux"},
              {"role":"assistant","content":"hi"}
            ]}
            """, 1);

        Assert.Equal(2, msgs.Count);
        Assert.Equal("user", (string?)msgs[0]!["role"]);
        var content = msgs[0]!["content"]!.AsArray();
        Assert.Equal("hello", (string?)content[0]!["text"]);
        Assert.Equal(Reminder("# Environment\nlinux"), (string?)content[1]!["text"]);
        Assert.Equal("assistant", (string?)msgs[1]!["role"]);
    }

    [Fact]
    public void Leading_System_Is_Merged_Into_Next_User_Turn()
    {
        var msgs = Fold("""
            {"model":"m","messages":[
              {"role":"system","content":[{"type":"text","text":"a"},{"type":"text","text":"b"}]},
              {"role":"user","content":[{"type":"text","text":"hello"}]}
            ]}
            """, 1);

        var only = Assert.Single(msgs);
        var content = only!["content"]!.AsArray();
        Assert.Equal("hello", (string?)content[0]!["text"]);
        Assert.Equal(Reminder("a\nb"), (string?)content[1]!["text"]);
    }

    [Fact]
    public void System_Between_Assistant_Turns_Becomes_User_Turn()
    {
        var msgs = Fold("""
            {"model":"m","messages":[
              {"role":"user","content":"q"},
              {"role":"assistant","content":"a"},
              {"role":"system","content":"hook output"},
              {"role":"assistant","content":"b"}
            ]}
            """, 1);

        Assert.Equal(new[] { "user", "assistant", "user", "assistant" }, msgs.Select(m => (string?)m!["role"]).ToArray());
        Assert.Equal(Reminder("hook output"), (string?)msgs[2]!["content"]![0]!["text"]);
    }

    [Fact]
    public void Trailing_System_After_Assistant_Becomes_Final_User_Turn()
    {
        var msgs = Fold("""
            {"model":"m","messages":[
              {"role":"user","content":"q"},
              {"role":"assistant","content":"a"},
              {"role":"system","content":"x"},
              {"role":"system","content":"y"}
            ]}
            """, 2);

        Assert.Equal(3, msgs.Count);
        var last = msgs[2]!["content"]!.AsArray();
        Assert.Equal("user", (string?)msgs[2]!["role"]);
        Assert.Equal(new[] { Reminder("x"), Reminder("y") }, last.Select(b => (string?)b!["text"]).ToArray());
    }

    [Fact]
    public void Other_Fields_And_Blocks_Survive()
    {
        var result = SystemMessageFolder.Fold(Body("""
            {"model":"m","max_tokens":32000,"stream":true,"system":[{"type":"text","text":"sys","cache_control":{"type":"ephemeral"}}],
             "messages":[
               {"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"ok"}]},
               {"role":"system","content":"r"}
             ],"tools":[{"name":"Bash"}]}
            """), out _);
        var root = JsonNode.Parse(result!)!;

        Assert.Equal(32000, (int)root["max_tokens"]!);
        Assert.True((bool)root["stream"]!);
        Assert.Equal("sys", (string?)root["system"]![0]!["text"]);
        Assert.Equal("Bash", (string?)root["tools"]![0]!["name"]);
        var content = root["messages"]![0]!["content"]!.AsArray();
        Assert.Equal("tool_result", (string?)content[0]!["type"]); // tool_result stays first
        Assert.Equal(Reminder("r"), (string?)content[1]!["text"]);
    }

    [Fact]
    public void Non_Ascii_Is_Not_Escaped()
    {
        var result = SystemMessageFolder.Fold(Body("""{"messages":[{"role":"user","content":"ü"},{"role":"system","content":"<ç>"}]}"""), out _);
        string s = Encoding.UTF8.GetString(result!);
        Assert.Contains("<ç>", s);
        Assert.Contains("ü", s);
    }
}
