using System;
using Veron;
using Xunit;

namespace Veron.Tests;

public class ServerTrackingTests
{
    [Fact]
    public void Paths_ServersDir_Is_Under_VeronDir()
    {
        var expected = System.IO.Path.Join(Paths.VeronDir, "servers");
        Assert.Equal(expected, Paths.ServersDir);
    }

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

    [Fact]
    public void StateManager_StateFile_Path_Is_Correct()
    {
        var expected = System.IO.Path.Join(Paths.ServersDir, "qwopus.json");
        Assert.Equal(expected, StateManager.StateFilePath("qwopus"));
    }

    [Fact]
    public void StateManager_WriteAndRead_ServerState()
    {
        string testDir = System.IO.Path.GetTempPath();
        var state = new ServerState
        {
            Model = "test-model",
            From = "Test.gguf",
            Port = 9999,
            Context = 4096,
            Pid = -1, // not a real PID
            StartedAt = DateTime.UtcNow
        };

        string tmpPath = System.IO.Path.Join(testDir, $"veron-test-{Guid.NewGuid()}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        System.IO.File.WriteAllText(tmpPath, json);

        var read = System.Text.Json.JsonSerializer.Deserialize<ServerState>(json);

        Assert.Equal("test-model", read!.Model);
        Assert.Equal(9999, read.Port);
        Assert.Equal(4096, read.Context);

        System.IO.File.Delete(tmpPath);
    }

    [Fact]
    public void StateManager_ServerRunning_Returns_False_When_No_State_File()
    {
        bool result = StateManager.IsServerRunning("nonexistent-model");
        Assert.False(result);
    }

    [Fact]
    public void StateManager_GetState_Returns_Null_When_No_State_File()
    {
        var result = StateManager.GetState("nonexistent-model");
        Assert.Null(result);
    }

    [Fact]
    public void Serve_Writes_State_File_After_Start()
    {
        var state = new ServerState
        {
            Model = "test-model",
            From = "Test.gguf",
            Port = 9999,
            Context = 4096,
            Pid = 1, // init process always exists
            StartedAt = DateTime.UtcNow
        };

        StateManager.WriteState(state);

        var path = System.IO.Path.Join(Paths.ServersDir, "test-model.json");
        Assert.True(System.IO.File.Exists(path));

        var read = StateManager.GetState("test-model");
        Assert.NotNull(read);
        Assert.Equal(9999, read.Port);

        // Cleanup
        StateManager.DeleteState("test-model");
    }
}
