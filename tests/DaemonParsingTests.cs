using System;
using System.IO;
using System.Threading;
using Veron;
using Veron.Daemon;
using Xunit;

namespace Veron.Tests;

public class ArgSplitterTests
{
    [Fact]
    public void Splits_On_Whitespace()
    {
        Assert.Equal(new[] { "--split-mode", "tensor", "--spec-draft-n-max", "4" },
                     ArgSplitter.Split("  --split-mode tensor   --spec-draft-n-max 4 "));
    }

    [Fact]
    public void Honours_Quotes_And_Escapes()
    {
        Assert.Equal(new[] { "--system-prompt", "be brief", "it's", "a\"b", "x y" },
                     ArgSplitter.Split("--system-prompt \"be brief\" \"it's\" 'a\"b' x\\ y"));
    }

    [Fact]
    public void Unbalanced_Quote_Throws()
    {
        Assert.Throws<FormatException>(() => ArgSplitter.Split("--foo \"bar"));
    }
}

public class KeepAliveTests
{
    [Theory]
    [InlineData("5m", 300)]
    [InlineData("30s", 30)]
    [InlineData("1h", 3600)]
    [InlineData("90", 90)]
    [InlineData("0", 0)]
    [InlineData("1.5m", 90)]
    public void Parses_Durations(string text, double seconds)
    {
        Assert.True(KeepAlive.TryParse(text, out var v));
        Assert.Equal(TimeSpan.FromSeconds(seconds), v);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-5m")]
    public void Negative_Means_Forever(string text)
    {
        Assert.True(KeepAlive.TryParse(text, out var v));
        Assert.True(KeepAlive.IsForever(v));
        Assert.Equal(Timeout.InfiniteTimeSpan, v);
    }

    [Theory]
    [InlineData("8s", "8s")]
    [InlineData("5m", "5m")]
    [InlineData("90s", "90s")]
    [InlineData("1h", "1h")]
    [InlineData("90m", "1h30m")]
    [InlineData("-1", "forever")]
    public void Formats_Durations(string text, string expected) =>
        Assert.Equal(expected, KeepAlive.Format(KeepAlive.Parse(text)));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("5x")]
    public void Rejects_Garbage(string text) => Assert.False(KeepAlive.TryParse(text, out _));
}

public class DaemonAddressTests
{
    [Theory]
    [InlineData(null, "127.0.0.1", 11435)]
    [InlineData("", "127.0.0.1", 11435)]
    [InlineData("0.0.0.0", "0.0.0.0", 11435)]
    [InlineData("localhost:9000", "localhost", 9000)]
    [InlineData(":9001", "127.0.0.1", 9001)]
    [InlineData("http://10.0.0.5:8080/", "10.0.0.5", 8080)]
    public void Parses_Host_Values(string? value, string host, int port)
    {
        Assert.Equal((host, port), DaemonAddress.Parse(value));
    }

    [Fact]
    public void Invalid_Port_Throws() => Assert.Throws<FormatException>(() => DaemonAddress.Parse("host:notaport"));
}

/// <summary>Temp modelfiles + models dirs with dummy .gguf files.</summary>
public sealed class TempProfiles : IDisposable
{
    public string Root { get; } = Path.Join(Path.GetTempPath(), "veron-test-" + Guid.NewGuid().ToString("n"));
    public string ModelfilesDir => Path.Join(Root, "modelfiles");
    public string ModelsDir => Path.Join(Root, "models");
    public string LogsDir => Path.Join(Root, "logs");

    public TempProfiles()
    {
        Directory.CreateDirectory(ModelfilesDir);
        Directory.CreateDirectory(ModelsDir);
    }

    public string Add(string name, string gguf, string extra = "")
    {
        File.WriteAllText(Path.Join(ModelsDir, gguf), "dummy");
        string path = Path.Join(ModelfilesDir, name);
        File.WriteAllText(path, $"FROM {gguf}\n{extra}\n");
        return path;
    }

    public ModelRegistry Registry() => new(ModelfilesDir, ModelsDir);

    public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
}

public class ModelfileDaemonParamsTests
{
    [Fact]
    public void Args_Lines_Accumulate_And_KeepAlive_Parses()
    {
        using var t = new TempProfiles();
        string path = t.Add("fast", "fast.gguf",
            "PARAMETER args \"--split-mode tensor --spec-type draft-mtp\"\n" +
            "PARAMETER args --spec-draft-p-min 0\n" +
            "PARAMETER keep_alive 10m");

        var cfg = ModelfileParser.ParseModelfile(path, t.ModelsDir);

        Assert.Equal(new[] { "--split-mode", "tensor", "--spec-type", "draft-mtp", "--spec-draft-p-min", "0" }, cfg.ExtraArgs);
        Assert.Equal(TimeSpan.FromMinutes(10), cfg.KeepAlive);
    }

    [Fact]
    public void Tool_Block_Parameters_Do_Not_Leak_Into_Server_Config()
    {
        using var t = new TempProfiles();
        string path = t.Add("p", "p.gguf",
            "PARAMETER context 4096\nTOOL claude-code\n  PARAMETER context 99\n  PARAMETER args --nope\nEND_TOOL");

        var cfg = ModelfileParser.ParseModelfile(path, t.ModelsDir);

        Assert.Equal(4096, cfg.Context);
        Assert.Empty(cfg.ExtraArgs);
    }

    [Fact]
    public void Default_Wait_Is_Long_Enough_For_Big_Models()
    {
        using var t = new TempProfiles();
        var cfg = ModelfileParser.ParseModelfile(t.Add("p", "p.gguf"), t.ModelsDir);
        Assert.Equal(120, cfg.Wait);
        Assert.Null(cfg.KeepAlive);
    }

    [Fact]
    public void BuildLlamaCmd_Appends_Extra_Args_Last_And_Binds_Localhost()
    {
        var cfg = new ModelConfig { ModelPath = "/m/x.gguf", Alias = "x", Port = 4321, NGpuLayers = 99 };
        cfg.ExtraArgs.AddRange(new[] { "--split-mode", "tensor" });

        var cmd = LlamaServer.BuildLlamaCmd(cfg);

        Assert.Equal(LlamaServer.BinaryPath, cmd[0]);
        Assert.Equal("127.0.0.1", cmd[cmd.IndexOf("--host") + 1]);
        Assert.Equal("4321", cmd[cmd.IndexOf("--port") + 1]);
        Assert.Equal("99", cmd[cmd.IndexOf("-ngl") + 1]);
        Assert.Equal(new[] { "--split-mode", "tensor" }, cmd.GetRange(cmd.Count - 2, 2));
    }

    [Fact]
    public void Validator_Accepts_New_Keys_And_Rejects_Bad_Values()
    {
        using var t = new TempProfiles();
        string ok  = t.Add("ok", "ok.gguf", "PARAMETER keep_alive -1\nPARAMETER args \"--split-mode tensor\"");
        string bad = t.Add("bad", "bad.gguf", "PARAMETER keep_alive soon");
        string badArgs = t.Add("badargs", "badargs.gguf", "PARAMETER args --x \"unterminated");

        Assert.Empty(ModelfileValidator.ValidateModelfile(ok, "ok", t.ModelsDir));
        Assert.Contains("keep_alive", ModelfileValidator.ValidateModelfile(bad, "bad", t.ModelsDir)[0]);
        Assert.Contains("args", ModelfileValidator.ValidateModelfile(badArgs, "badargs", t.ModelsDir)[0]);
    }
}

public class ModelRegistryTests
{
    [Fact]
    public void Resolves_By_Profile_Name_Then_Alias_Case_Insensitive()
    {
        using var t = new TempProfiles();
        t.Add("qwen-fast", "q.gguf", "PARAMETER alias \"Qwen3.8-27B\"");

        var reg = t.Registry();

        Assert.Equal("qwen-fast", reg.Resolve("qwen-fast")!.Name);
        Assert.Equal("qwen-fast", reg.Resolve("QWEN-FAST")!.Name);
        Assert.Equal("qwen-fast", reg.Resolve("qwen3.8-27b")!.Name);
        Assert.Null(reg.Resolve("claude-haiku-4-5"));
        Assert.Null(reg.Resolve("../etc/passwd"));
    }

    [Fact]
    public void Lists_Only_Servable_Profiles_And_Sees_New_Files_Immediately()
    {
        using var t = new TempProfiles();
        t.Add("a", "a.gguf");
        File.WriteAllText(Path.Join(t.ModelfilesDir, "broken"), "FROM missing.gguf\n");

        var reg = t.Registry();
        Assert.Equal(new[] { "a" }, reg.List().Select(m => m.Name));

        t.Add("b", "b.gguf");
        Assert.Equal(new[] { "a", "b" }, reg.List().Select(m => m.Name));
    }
}
