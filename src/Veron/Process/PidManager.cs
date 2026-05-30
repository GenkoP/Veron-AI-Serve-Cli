using System;
using System.Diagnostics;
using System.IO;

namespace Veron;

static class PidManager
{
    // These paths are shared between PidManager and Program — kept inline here as constants
    static string VeronDir => Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".veron");
    static string ModelfilesDir => Path.Join(VeronDir, "modelfiles");
    static string PidFile => Path.Join(VeronDir, "veron.pid");

    public static void WritePid(int pid)
    {
        Directory.CreateDirectory(VeronDir);
        File.WriteAllText(PidFile, pid.ToString());
    }

    public static bool ReadPid(out int pid)
    {
        if (File.Exists(PidFile))
        {
            string text = File.ReadAllText(PidFile).Trim();
            if (int.TryParse(text, out int p)) { pid = p; return true; }
        }
        pid = 0;
        return false;
    }

    public static void DeletePid() => File.Delete(PidFile);

    public static bool IsProcessAlive(int pid)
    {
        try { Process.GetProcessById(pid); return true; }
        catch { return false; }
    }
}
