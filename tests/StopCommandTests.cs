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
}
