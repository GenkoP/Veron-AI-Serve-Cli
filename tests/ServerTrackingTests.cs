using System;
using Veron;
using Xunit;

namespace Veron.Tests;

public class ServerTrackingTests
{
    [Fact]
    public void ServerState_Has_Required_Fields()
    {
        var state = new ServerState
        {
            Model = "qwopus",
            From = "Qwopus3.6-27B.gguf",
            Port = 5570,
            Context = 128000,
            Pid = 12345,
            StartedAt = new DateTime(2026, 5, 30, 15, 30, 0, DateTimeKind.Utc)
        };

        Assert.Equal("qwopus", state.Model);
        Assert.Equal("Qwopus3.6-27B.gguf", state.From);
        Assert.Equal(5570, state.Port);
        Assert.Equal(128000, state.Context);
        Assert.Equal(12345, state.Pid);
    }
}
