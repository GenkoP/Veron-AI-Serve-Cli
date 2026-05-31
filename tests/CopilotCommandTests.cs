using System;
using Veron;
using Xunit;

namespace Veron.Tests;

public class CopilotCommandTests
{
    // ── Validator Tests ─────────────────────────────────────────────

    [Fact]
    public void Validator_Effort_ValidValues_Pass()
    {
        foreach (var val in new[] { "none", "low", "medium", "high", "xhigh", "max" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("effort", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_Effort_InvalidValue_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("effort", "ultra");
        Assert.NotEmpty(errors);
        Assert.Contains("invalid value", errors[0]);
    }

    [Fact]
    public void Validator_Mode_ValidValues_Pass()
    {
        foreach (var val in new[] { "interactive", "plan", "autopilot" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("mode", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_Mode_InvalidValue_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("mode", "custom");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validator_LogLevel_ValidValues_Pass()
    {
        foreach (var val in new[] { "none", "error", "warning", "info", "debug", "all", "default" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("log-level", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_LogLevel_InvalidValue_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("log-level", "verbose");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validator_Stream_ValidValues_Pass()
    {
        foreach (var val in new[] { "on", "off" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("stream", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_Stream_InvalidValue_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("stream", "yes");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validator_OutputFormat_ValidValues_Pass()
    {
        foreach (var val in new[] { "text", "json" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("output-format", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_BashEnv_ValidValues_Pass()
    {
        foreach (var val in new[] { "on", "off" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("bash-env", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_Mouse_ValidValues_Pass()
    {
        foreach (var val in new[] { "on", "off" })
        {
            var errors = CopilotValidator.ValidateCopilotParameter("mouse", val);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validator_ReasoningEffort_Alias_For_Effort()
    {
        // reasoning-effort is an alias for effort — same valid values
        var errors = CopilotValidator.ValidateCopilotParameter("reasoning-effort", "high");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validator_MaxAutopilotContinues_Integer_Pass()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("max-autopilot-continues", "10");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validator_MaxAutopilotContinues_NonInteger_Fails()
    {
        var errors = CopilotValidator.ValidateCopilotParameter("max-autopilot-continues", "ten");
        Assert.NotEmpty(errors);
        Assert.Contains("expected an integer", errors[0]);
    }

    [Fact]
    public void Validator_UnknownParam_PassesThrough()
    {
        // Unknown parameters should pass through without validation
        var errors = CopilotValidator.ValidateCopilotParameter("some-future-flag", "anything");
        Assert.Empty(errors);
    }

    // ── Server State Management Tests ──────────────────────────────

    [Fact]
    public void StateManager_StopServer_Returns_False_For_Nonexistent_Copilot()
    {
        bool result = StateManager.StopServer("nonexistent-copilot-test");
        Assert.False(result);
    }

    [Fact]
    public void StateManager_WriteAndStop_Copilot_ServerState()
    {
        string modelName = "copilot-test-model";

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
    public void StateManager_DeleteState_Copilot_Removes_File()
    {
        string modelName = "copilot-delete-test";

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
}
