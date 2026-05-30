using System.Diagnostics;

namespace Veron;

static class CmdStop
{
    public static void Run()
    {
        if (!PidManager.ReadPid(out int pid))
        {
            Console.WriteLine("No saved PID — llama-server may not have been started by veron.");
            return;
        }

        if (!PidManager.IsProcessAlive(pid))
        {
            Console.WriteLine("Process " + pid + " is no longer running. Cleaning up.");
            PidManager.DeletePid();
            return;
        }

        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill(true);
            Console.WriteLine("Stopped llama-server (PID " + pid + ").");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("Process " + pid + " already gone.");
        }
        finally { PidManager.DeletePid(); }
    }
}
