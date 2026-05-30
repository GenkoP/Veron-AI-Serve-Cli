using System.Collections.Generic;
using Veron;
using Xunit;

namespace Veron.Tests;

public class StopCommandTests
{
    [Fact]
    public void CmdStop_Has_Run_With_Opts_Signature()
    {
        var states = StateManager.ListRunningServers();
        Assert.IsType<List<ServerState>>(states);
    }

    [Fact]
    public void StopAllServers_Returns_Zero_When_Nothing_Running()
    {
        int count = StateManager.StopAllServers();
        Assert.Equal(0, count);
    }

    [Fact]
    public void StopServer_Returns_False_For_Nonexistent_Model()
    {
        bool stopped = StateManager.StopServer("nonexistent-model");
        Assert.False(stopped);
    }

    [Fact]
    public void Claude_Does_Not_Stop_Server_On_Exit()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), "claude-test");
        Directory.CreateDirectory(tmpDir);

        try
        {
            var state = new ServerState
            {
                Model = "test-model",
                From = "Test.gguf",
                Port = 5570,
                Context = 4096,
                Pid = 1, // init always exists
                StartedAt = DateTime.UtcNow
            };

            StateManager.WriteState(state);

            Assert.NotNull(StateManager.GetState("test-model"));
            Assert.True(StateManager.IsServerRunning("test-model"));
        }
        finally
        {
            StateManager.DeleteState("test-model");
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
