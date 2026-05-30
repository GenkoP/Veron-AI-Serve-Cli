using System;
using System.Diagnostics;
using System.IO;

namespace Veron;

static class TerminalDetector
{
    public static string? DetectTerminal()
    {
        string? termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        if (!string.IsNullOrEmpty(termProgram))
            return MapTerminalName(termProgram);

        string? terminal = Environment.GetEnvironmentVariable("TERMINAL");
        if (!string.IsNullOrEmpty(terminal))
            return MapTerminalName(terminal);

        return DetectFromProcessTree();
    }

    static string? MapTerminalName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "gnome-terminal" or "gnome" => "gnome-terminal",
            "konsole" => "konsole",
            "xfce4-terminal" => "xfce4-terminal",
            "xterm" => "xterm",
            _ => null,
        };
    }

    static string? DetectFromProcessTree()
    {
        try
        {
            int currentPid = Process.GetCurrentProcess().Id;

            for (int depth = 0; depth < 10; depth++)
            {
                using var proc = Process.GetProcessById(currentPid);

                try
                {
                    string processName = proc.ProcessName.ToLowerInvariant();

                    if (processName.Contains("gnome-terminal")) return "gnome-terminal";
                    if (processName.Contains("konsole")) return "konsole";
                    if (processName.Contains("xfce4-terminal")) return "xfce4-terminal";
                    if (processName.Contains("xterm") || processName.Contains("x-terminal")) return "xterm";

                    currentPid = GetParentPid(currentPid);
                    if (currentPid <= 0) break;
                }
                catch
                {
                    break;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    static int GetParentPid(int pid)
    {
        try
        {
            string statusPath = Path.Join("/proc", pid.ToString(), "status");
            if (!File.Exists(statusPath)) return -1;

            foreach (var line in File.ReadLines(statusPath))
            {
                if (line.StartsWith("PPid:"))
                {
                    string value = line[5..].Trim();
                    if (int.TryParse(value, out int ppid)) return ppid;
                }
            }
        }
        catch { }

        return -1;
    }

    public static string? BuildTerminalCommand(string? terminal, string command)
    {
        return terminal switch
        {
            "gnome-terminal" => $"-- {command}",
            "konsole" => $"-e {command}",
            "xfce4-terminal" => $"-x {command}",
            "xterm" => $"-e {command}",
            _ => null,
        };
    }
}
