using System.Diagnostics;
using System.IO;

namespace Veron;

static class PidManager
{
    public static void WritePid(int pid)
    {
        Directory.CreateDirectory(Paths.VeronDir);
        File.WriteAllText(Paths.PidFile, pid.ToString());
    }

    public static bool ReadPid(out int pid)
    {
        if (File.Exists(Paths.PidFile))
        {
            string text = File.ReadAllText(Paths.PidFile).Trim();
            if (int.TryParse(text, out int p)) { pid = p; return true; }
        }
        pid = 0;
        return false;
    }

    public static void DeletePid() => File.Delete(Paths.PidFile);

    public static bool IsProcessAlive(int pid)
    {
        try { Process.GetProcessById(pid); return true; }
        catch { return false; }
    }
}
