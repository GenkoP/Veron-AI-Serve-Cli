using Veron;
using Xunit;

namespace Veron.Tests;

public class VersionCommandTests
{
    const string RealOutput =
        "version: 1.1.0-dev (build 12815, commit 2a8bf0f59)\nbuilt with GNU 15.2.0 for Linux x86_64\n";

    [Fact]
    public void ParseVersion_Reads_LlamaCpp_Output()
    {
        Assert.Equal("1.1.0-dev (build 12815, commit 2a8bf0f59)", LlamaServer.ParseVersion(RealOutput));
    }

    [Fact]
    public void ParseVersion_Skips_Leading_Lines_And_CRLF()
    {
        Assert.Equal("b6000 (abc123)", LlamaServer.ParseVersion("ggml_cuda_init: found 2 CUDA devices\r\nversion: b6000 (abc123)\r\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("built with GNU 15.2.0 for Linux x86_64\n")]
    [InlineData("version:   \n")]
    public void ParseVersion_Returns_Null_Without_Version(string output)
    {
        Assert.Null(LlamaServer.ParseVersion(output));
    }

    [Fact]
    public void QueryVersion_Returns_Null_For_Missing_Binary()
    {
        Assert.Null(LlamaServer.QueryVersion(Path.Join(Path.GetTempPath(), "veron-no-such-llama-server-" + Guid.NewGuid())));
    }

    [Fact]
    public void QueryVersion_Reads_Stderr_Of_Binary()
    {
        if (OperatingSystem.IsWindows()) return;
        string dir = Path.Join(Path.GetTempPath(), "veron-version-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string script = Path.Join(dir, "llama-server");
            File.WriteAllText(script, "#!/bin/sh\n[ \"$1\" = \"--version\" ] || exit 2\necho 'version: 9.9.9 (build 1, commit deadbee)' >&2\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Assert.Equal("9.9.9 (build 1, commit deadbee)", LlamaServer.QueryVersion(script));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FormatVersion_Shows_Both_Versions_And_Binary()
    {
        string text = Program.FormatVersion("1.1.0-dev (build 12815, commit 2a8bf0f59)", "/opt/llama/llama-server");
        Assert.Equal("veron " + Program.Version + "\nllama.cpp 1.1.0-dev (build 12815, commit 2a8bf0f59)\n  /opt/llama/llama-server", text);
    }

    [Fact]
    public void FormatVersion_Explains_Unknown_LlamaCpp()
    {
        string text = Program.FormatVersion(null, "/opt/llama/llama-server");
        Assert.StartsWith("veron " + Program.Version + "\n", text);
        Assert.Contains("llama.cpp unknown", text);
        Assert.Contains("/opt/llama/llama-server", text);
        Assert.Contains("VERON_LLAMA_SERVER", text);
    }
}
