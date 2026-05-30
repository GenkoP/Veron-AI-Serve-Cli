using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Veron;

static class StateManager
{
    const string JsonExtension = ".json";

    public static string StateFilePath(string modelName) =>
        Path.Join(Paths.ServersDir, modelName + JsonExtension);

    public static void WriteState(ServerState state)
    {
        Directory.CreateDirectory(Paths.ServersDir);
        string path = StateFilePath(state.Model);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static ServerState? GetState(string modelName)
    {
        string path = StateFilePath(modelName);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ServerState>(json);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsServerRunning(string modelName)
    {
        var state = GetState(modelName);
        if (state is null) return false;

        if (!PidManager.IsProcessAlive(state.Pid))
        {
            DeleteState(modelName);
            return false;
        }

        return true;
    }

    public static void DeleteState(string modelName)
    {
        string path = StateFilePath(modelName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static List<ServerState> ListRunningServers()
    {
        var result = new List<ServerState>();

        if (!Directory.Exists(Paths.ServersDir)) return result;

        foreach (var file in Directory.GetFiles(Paths.ServersDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var state = JsonSerializer.Deserialize<ServerState>(json);

                if (state is not null && PidManager.IsProcessAlive(state.Pid))
                {
                    result.Add(state);
                }
                else
                {
                    File.Delete(file);
                }
            }
            catch
            {
                try { File.Delete(file); } catch { }
            }
        }

        return result;
    }

    public static bool StopServer(string modelName)
    {
        var state = GetState(modelName);
        if (state is null) return false;

        if (!PidManager.IsProcessAlive(state.Pid))
        {
            DeleteState(modelName);
            return false;
        }

        try
        {
            using var proc = Process.GetProcessById(state.Pid);
            proc.Kill(true);
            Console.WriteLine("Stopped llama-server for " + state.Model + " (PID " + state.Pid + ").");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("Process " + state.Pid + " already gone.");
        }
        finally
        {
            DeleteState(modelName);
        }

        return true;
    }

    public static int StopAllServers()
    {
        var running = ListRunningServers();
        int count = 0;

        foreach (var state in running)
        {
            try
            {
                using var proc = Process.GetProcessById(state.Pid);
                proc.Kill(true);
                Console.WriteLine("Stopped llama-server for " + state.Model + " (PID " + state.Pid + ").");
                count++;
            }
            catch (ArgumentException)
            {
                // Already gone
            }
            finally
            {
                DeleteState(state.Model);
            }
        }

        return count;
    }
}
