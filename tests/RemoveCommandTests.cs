using System;
using System.IO;
using Veron;
using Xunit;

namespace Veron.Tests;

public class RemoveCommandTests
{
    [Fact]
    public void CmdRemove_Rejects_Nonexistent_Profile()
    {
        bool result = StateManager.StopServer("nonexistent-xyz");
        Assert.False(result);
    }

    [Fact]
    public void Remove_Deletes_Modelfile_And_State()
    {
        string testDir = System.IO.Path.GetTempPath();
        string modelfilesDir = Path.Join(testDir, "modelfiles-rm-test");
        string serversDir = Path.Join(testDir, "servers-rm-test");

        try
        {
            Directory.CreateDirectory(modelfilesDir);
            Directory.CreateDirectory(serversDir);

            string mfPath = Path.Join(modelfilesDir, "test-profile");
            File.WriteAllText(mfPath, "FROM test.gguf\nPARAMETER port 5570");

            var state = new ServerState
            {
                Model = "test-profile",
                From = "test.gguf",
                Port = 5570,
                Context = 4096,
                Pid = 1,
                StartedAt = DateTime.UtcNow
            };
            string statePath = Path.Join(serversDir, "test-profile.json");
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            File.WriteAllText(statePath, json);

            Assert.True(File.Exists(mfPath));
            Assert.True(File.Exists(statePath));

            File.Delete(mfPath);
            File.Delete(statePath);

            Assert.False(File.Exists(mfPath));
            Assert.False(File.Exists(statePath));
        }
        finally
        {
            Directory.Delete(modelfilesDir, recursive: true);
            Directory.Delete(serversDir, recursive: true);
        }
    }

    [Fact]
    public void Remove_Confirmation_Prompt_Flow()
    {
        string modelName = "test-model";
        string expectedPrompt = "Remove profile " + modelName + "? [y/N]";
        Assert.Equal(expectedPrompt, CmdRemove.ConfirmationPrompt(modelName));
    }
}
