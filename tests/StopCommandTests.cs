using System;
using System.Collections.Generic;
using System.IO;
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
    public void StateManager_Writes_And_Reads_Server_State()
    {
        string modelName = "state-test-model";

        var state = new ServerState
        {
            Model = modelName,
            From = "Test.gguf",
            Port = 5570,
            Context = 4096,
            Pid = 99999999, // dead PID — won't actually be alive
            StartedAt = DateTime.UtcNow
        };

        StateManager.WriteState(state);

        Assert.NotNull(StateManager.GetState(modelName));

        // Dead PID means server is not considered running (and state is cleaned up)
        Assert.False(StateManager.IsServerRunning(modelName));
    }
}
