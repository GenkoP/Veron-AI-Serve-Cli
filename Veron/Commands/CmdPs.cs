using System;
using System.Collections.Generic;
using System.Linq;

namespace Veron;

static class CmdPs
{
    public static void Run()
    {
        var servers = StateManager.ListRunningServers();

        if (servers.Count == 0)
        {
            Console.WriteLine("No servers currently running.");
            return;
        }

        // Column widths
        int nameWidth = Math.Max(6, servers.Max(s => s.Model.Length)) + 2;
        int fromWidth = Math.Max(8, servers.Max(s => s.From.Length)) + 2;
        int portWidth = Math.Max(5, "PORT".Length) + 2;
        int ctxWidth = Math.Max(7, "CONTEXT".Length) + 2;
        int pidWidth = Math.Max(6, "PID".Length) + 2;

        // Header
        Console.WriteLine(
            PadRight("NAME", nameWidth) +
            PadRight("MODEL FILE", fromWidth) +
            PadRight("PORT", portWidth) +
            PadRight("CONTEXT", ctxWidth) +
            PadRight("PID", pidWidth) +
            "STARTED");

        // Data rows
        foreach (var s in servers.OrderBy(x => x.StartedAt))
        {
            Console.WriteLine(
                PadRight(s.Model, nameWidth) +
                PadRight(s.From, fromWidth) +
                PadRight(s.Port.ToString(), portWidth) +
                PadRight(s.Context.ToString(), ctxWidth) +
                PadRight(s.Pid.ToString(), pidWidth) +
                FormatStartedTime(s.StartedAt));
        }
    }

    public static string FormatStartedTime(DateTime dt)
    {
        if (dt.Date == DateTime.Today)
            return dt.ToString("HH:mm");

        return dt.ToString("yyyy-MM-dd HH:mm");
    }

    static string PadRight(string value, int width)
    {
        return value.PadRight(width);
    }
}
