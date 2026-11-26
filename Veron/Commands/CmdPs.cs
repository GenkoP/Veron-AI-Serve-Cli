using System;
using System.Collections.Generic;
using System.Linq;
using Veron.Daemon;

namespace Veron;

/// <summary>`veron ps` — models currently loaded by the Veron server.</summary>
static class CmdPs
{
    public static void Run()
    {
        var client = DaemonClient.Default();
        if (!client.IsRunning())
        {
            Console.WriteLine("Veron server is not running.");
            return;
        }

        List<ModelStatus> models;
        try { models = client.Ps(); }
        catch (DaemonException ex) { Console.Error.WriteLine("Error: " + ex.Message); Environment.Exit(1); return; }

        if (models.Count == 0)
        {
            Console.WriteLine("No models loaded (Veron server at " + client.BaseUrl + ").");
            return;
        }

        var now  = DateTimeOffset.Now;
        var rows = models.Select(m => new[]
        {
            m.Name, m.From, m.Pid.ToString(), m.Context.ToString(), FormatStartedTime(m.LoadedAt.LocalDateTime),
            FormatUntil(m, now),
        }).ToList();
        string[] header = { "NAME", "FROM", "PID", "CONTEXT", "LOADED", "UNTIL" };

        var widths = header.Select((h, i) => Math.Max(h.Length, rows.Max(r => r[i].Length)) + 3).ToArray();
        Console.WriteLine(string.Concat(header.Select((h, i) => i == header.Length - 1 ? h : h.PadRight(widths[i]))));
        foreach (var r in rows)
            Console.WriteLine(string.Concat(r.Select((v, i) => i == r.Length - 1 ? v : v.PadRight(widths[i]))));
    }

    public static string FormatStartedTime(DateTime dt)
    {
        if (dt.Date == DateTime.Today)
            return dt.ToString("HH:mm");

        return dt.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>When the model will be unloaded, Ollama style.</summary>
    public static string FormatUntil(ModelStatus m, DateTimeOffset now)
    {
        if (m.Sessions > 0)   return "In use (" + m.Sessions + (m.Sessions == 1 ? " session)" : " sessions)");
        if (m.KeepForever)    return "Forever";
        if (m.ActiveRequests > 0 || m.ExpiresAt is null) return "Busy";

        var left = m.ExpiresAt.Value - now;
        if (left <= TimeSpan.Zero)       return "Unloading...";
        if (left < TimeSpan.FromMinutes(1)) return (int)Math.Ceiling(left.TotalSeconds) + " seconds from now";
        if (left < TimeSpan.FromHours(1))
        {
            int min = (int)Math.Round(left.TotalMinutes);
            return min + (min == 1 ? " minute from now" : " minutes from now");
        }
        int hours = (int)Math.Round(left.TotalHours);
        return hours + (hours == 1 ? " hour from now" : " hours from now");
    }
}
