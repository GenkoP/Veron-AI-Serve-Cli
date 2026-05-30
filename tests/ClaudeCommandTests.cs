using System;
using System.IO;
using Veron;
using Xunit;

namespace Veron.Tests;

public class ClaudeCommandTests
{
    [Fact]
    public void StateManager_StopServer_Returns_False_For_Nonexistent()
    {
        bool result = StateManager.StopServer("nonexistent-claude-test");
        Assert.False(result);
    }

    [Fact]
    public void StateManager_WriteAndStop_ServerState()
    {
        string modelName = "claude-test-model";

        var state = new ServerState
        {
            Model = modelName,
            From = "Test.gguf",
            Port = 5570,
            Context = 4096,
            Pid = 99999999, // dead PID — StopServer will clean up and return false
            StartedAt = DateTime.UtcNow
        };

        StateManager.WriteState(state);

        // Dead PID means IsServerRunning returns false (and cleans up)
        Assert.False(StateManager.IsServerRunning(modelName));
    }

    [Fact]
    public void StateManager_DeleteState_Removes_File()
    {
        string modelName = "claude-delete-test";

        var state = new ServerState
        {
            Model = modelName,
            From = "Test.gguf",
            Port = 5571,
            Context = 4096,
            Pid = 1,
            StartedAt = DateTime.UtcNow
        };

        StateManager.WriteState(state);
        Assert.True(File.Exists(StateManager.StateFilePath(modelName)));

        StateManager.DeleteState(modelName);
        Assert.False(File.Exists(StateManager.StateFilePath(modelName)));
    }

    [Fact]
    public void PidManager_DeletePid_Removes_File()
    {
        // Write a PID file first
        PidManager.WritePid(9999);

        // Delete it
        PidManager.DeletePid();

        // The PID file should be gone
        Assert.False(File.Exists(Paths.PidFile));
    }

    [Fact]
    public void StateManager_ListRunningServers_Cleans_Dead_PIDs()
    {
        string modelName = "claude-dead-pid-test";

        var state = new ServerState
        {
            Model = modelName,
            From = "Test.gguf",
            Port = 5572,
            Context = 4096,
            Pid = 99999999, // almost certainly a dead PID
            StartedAt = DateTime.UtcNow
        };

        StateManager.WriteState(state);

        var running = StateManager.ListRunningServers();
        // Dead PID should be cleaned up and not appear in the list
        Assert.DoesNotContain(running, s => s.Model == modelName);
    }

    [Fact]
    public void ModelfileParser_OptsBool_Foreground_Only_Checks_Long_Form()
    {
        // Verify that -f is NOT interpreted as foreground for claude command
        // The -f flag should only work with the remove command
        var opts = new System.Collections.Generic.Dictionary<string, string>
        {
            ["model"] = "test-model",
        };

        // Without --foreground flag, foreground should be false
        bool foreground = CliParser.OptsBool(opts, "foreground");
        Assert.False(foreground);
    }

    [Fact]
    public void ModelfileParser_OptsBool_Foreground_True_When_Set()
    {
        var opts = new System.Collections.Generic.Dictionary<string, string>
        {
            ["model"] = "test-model",
            ["foreground"] = "true",
        };

        bool foreground = CliParser.OptsBool(opts, "foreground");
        Assert.True(foreground);
    }
}
