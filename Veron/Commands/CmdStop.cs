using System.Collections.Generic;

namespace Veron;

static class CmdStop
{
    public static void Run(Dictionary<string, string> opts)
    {
        string? modelName = opts.GetValueOrDefault("model");

        if (modelName is not null)
        {
            RunForModel(modelName);
        }
        else
        {
            RunAll();
        }
    }

    static void RunForModel(string modelName)
    {
        var state = StateManager.GetState(modelName);
        if (state is null || !PidManager.IsProcessAlive(state.Pid))
        {
            Console.WriteLine("No server running for " + modelName + ".");
            return;
        }

        StateManager.StopServer(modelName);
    }

    static void RunAll()
    {
        int count = StateManager.StopAllServers();
        if (count == 0)
        {
            Console.WriteLine("No servers currently running.");
        }
        else
        {
            Console.WriteLine(count + " server(s) stopped.");
        }
    }
}
