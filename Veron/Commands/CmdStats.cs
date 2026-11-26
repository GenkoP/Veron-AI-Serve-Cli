using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Veron.Daemon;

namespace Veron;

/// <summary>`veron stats [--watch|-w|watch]` — live view of the loaded model and recent requests.</summary>
static class CmdStats
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string NotRunning = "Veron server is not running.";

    public static bool IsWatch(IEnumerable<string> args) =>
        args.Any(a => a is "--watch" or "-w" or "watch");

    public const string OldDaemon = "The running Veron server is older than this CLI and has no stats — restart it with: veron shutdown";

    /// <summary>True when the daemon version is missing/unparseable or older than the CLI's.</summary>
    public static bool IsDaemonOlder(string? daemonVersion, string cliVersion)
    {
        if (!Version.TryParse(daemonVersion, out var d)) return true;
        return Version.TryParse(cliVersion, out var c) && d < c;
    }

    public static void Run(string[] args)
    {
        if (args.Any(a => a is not ("--watch" or "-w" or "watch" or "--help" or "-h")))
        {
            Console.Error.WriteLine("Usage: veron stats [--watch|-w]");
            Environment.Exit(1);
            return;
        }
        var client = DaemonClient.Default();
        if (IsWatch(args)) { Watch(client); return; }

        if (!client.IsRunning()) { Console.WriteLine(NotRunning); return; }
        if (IsDaemonOlder(client.DaemonVersion(), Program.Version))
        { Console.Error.WriteLine(OldDaemon); Environment.Exit(1); return; }
        StatsSnapshot snap;
        try { snap = client.Stats(); }
        catch (DaemonException ex) { Console.Error.WriteLine("Error: " + ex.Message); Environment.Exit(1); return; }
        Console.WriteLine(Render(snap, null, TerminalWidth(), watch: false, client.BaseUrl));
    }

    // ── Watch loop ───────────────────────────────────────────────────────────

    static void Watch(DaemonClient client)
    {
        bool tty = !Console.IsOutputRedirected;
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;

        var rate = new LiveRate(TimeSpan.FromSeconds(2));
        var seen = new HashSet<(int?, int)>();
        bool seeded = false;
        string? lastPhase = null;
        bool versionOk = false;

        if (tty) Console.Write("\x1b[?25l\x1b[2J");
        try
        {
            while (!cts.IsCancellationRequested)
            {
                StatsSnapshot? snap = null;
                string? message = null;
                if (!client.IsRunning())
                {
                    versionOk = false;
                    message = NotRunning.TrimEnd('.') + " — waiting…";
                }
                else
                {
                    // check the daemon version once per (re)connect; an old daemon is re-checked each poll
                    if (!versionOk)
                    {
                        if (IsDaemonOlder(client.DaemonVersion(), Program.Version)) message = OldDaemon;
                        else versionOk = true;
                    }
                    if (versionOk)
                    {
                        try { snap = client.Stats(); }
                        catch (DaemonException ex) { message = "Error: " + ex.Message; }
                    }
                }

                if (snap is null)
                {
                    rate.Reset();
                    if (tty) Draw(message ?? NotRunning);
                }
                else
                {
                    var slot = snap.Slot;
                    double? live = null;
                    string? phase = slot is null || !slot.IsProcessing ? null : slot.Decoded > 0 ? "gen" : "prompt";
                    if (phase != lastPhase) rate.Reset();
                    lastPhase = phase;
                    if (phase == "gen") live = rate.Add(snap.Now, slot!.Decoded);
                    else if (phase == "prompt") live = rate.Add(snap.Now, slot!.PromptTokens);
                    else rate.Reset();

                    if (tty)
                        Draw(Render(snap, live, TerminalWidth(), watch: true, client.BaseUrl));
                    else
                    {
                        var fresh = TakeNew(snap, seen);
                        if (seeded)
                            foreach (var t in fresh) Console.WriteLine(FormatLine(snap.Model ?? "", t));
                        seeded = true;
                    }
                }

                cts.Token.WaitHandle.WaitOne(500);
            }
        }
        catch (IOException) when (!tty) { /* reader closed the pipe (e.g. `| head -1`) */ }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            if (tty) Console.Write("\x1b[?25h\n");
        }
    }

    static void Draw(string screen)
    {
        var sb = new StringBuilder("\x1b[H");
        var lines = screen.Split('\n');
        int max = Math.Max(1, TerminalHeight() - 1);
        if (lines.Length > max) lines = lines[..max];
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines[i]).Append("\x1b[K");
        }
        sb.Append("\x1b[J");
        Console.Write(sb.ToString());
    }

    static int TerminalHeight()
    {
        try { int h = Console.WindowHeight; return h > 0 ? h : 40; }
        catch { return 40; }
    }

    static int TerminalWidth()
    {
        try { int w = Console.WindowWidth; return w > 0 ? w : 100; }
        catch { return 100; }
    }

    // ── Pure helpers ─────────────────────────────────────────────────────────

    /// <summary>Timings not yet in <paramref name="seen"/>, oldest first; marks them seen.</summary>
    public static List<RequestTiming> TakeNew(StatsSnapshot snap, HashSet<(int?, int)> seen) =>
        snap.Recent.Where(t => seen.Add((snap.Pid, t.Task))).Reverse().ToList();

    public static string FormatLine(string model, RequestTiming t)
    {
        var s = $"{Time(t.FinishedAt)}  {model}  prompt {N(t.PromptTokens)} tok @ {N(t.PromptTokensPerSecond)} t/s" +
                $"  output {N(t.OutputTokens)} tok @ {F1(t.OutputTokensPerSecond)} t/s";
        return t.DraftAcceptance is { } d ? s + "  draft " + Pct(d) : s;
    }

    public static string Render(StatsSnapshot snap, double? liveRate, int width, bool watch, string? baseUrl = null)
    {
        if (snap.Model is null)
            return "No model loaded (Veron server at " + (baseUrl ?? "?") + ").";

        var sb = new StringBuilder();
        string left  = $"Veron stats · {snap.Model} · ctx {N(snap.Context)}";
        string right = Time(snap.Now) + (watch ? "  (Ctrl+C to quit)" : "");
        sb.Append(left).Append(' ', Math.Max(2, width - 1 - left.Length - right.Length)).Append(right).Append("\n\n");

        var slot = snap.Slot;
        if (slot is null)
        {
            sb.Append("State      unknown (slot info unavailable)\n");
        }
        else
        {
            // /slots semantics: PromptTokens = tokens in the slot context so far (grows per batch while the
            // prompt is read, then includes generated tokens); Decoded = generated tokens, 0 until generation starts.
            bool generating  = slot.IsProcessing && slot.Decoded > 0;
            bool promptPhase = slot.IsProcessing && slot.Decoded == 0;
            string liveText = liveRate is { } r ? "  " + (promptPhase ? N(r) : F1(r)) + " t/s" : "";
            string state = promptPhase ? "● processing prompt  " + N(slot.PromptTokens) + " tokens so far" + liveText
                         : generating  ? "● generating"
                         : "○ idle";
            sb.Append("State      ").Append(state).Append('\n');

            sb.Append("Prompt     ");
            if (generating)
            {
                sb.Append(N(Math.Max(0, slot.PromptTokens - slot.Decoded))).Append(" tokens");
                if (slot.PromptTokensCached > 0) sb.Append("  (").Append(N(slot.PromptTokensCached)).Append(" from cache)");
            }
            else sb.Append('—');
            sb.Append('\n');

            int nctx = slot.NCtx > 0 ? slot.NCtx : snap.Context;
            string pct = nctx > 0 ? $"  ({(100.0 * slot.PromptTokens / nctx).ToString("0.0", Inv)}%)" : "";
            sb.Append("Context    ").Append(N(slot.PromptTokens)).Append(" / ").Append(N(nctx)).Append(pct).Append('\n');

            sb.Append("Output     ");
            if (generating)
                sb.Append(N(slot.Decoded)).Append(" tokens    ").Append(liveRate is { } g ? F1(g) + " t/s live" : "—");
            else sb.Append('—');
            sb.Append('\n');
        }

        sb.Append("\nRecent requests\n");
        if (snap.Recent.Count == 0) { sb.Append("(none yet)"); return sb.ToString(); }

        string[] header = { "TIME", "PROMPT", "PROMPT t/s", "OUTPUT", "OUTPUT t/s", "DRAFT ACCEPT" };
        var rows = snap.Recent.Select(t => new[]
        {
            Time(t.FinishedAt), N(t.PromptTokens), N(t.PromptTokensPerSecond), N(t.OutputTokens),
            F1(t.OutputTokensPerSecond), t.DraftAcceptance is { } d ? Pct(d) : "—",
        }).ToList();
        var w = header.Select((h, i) => Math.Max(h.Length, rows.Max(r => r[i].Length))).ToArray();
        string Row(string[] cells) => string.Join("   ", cells.Select((c, i) => i == 0 ? c.PadRight(w[i]) : c.PadLeft(w[i])));
        sb.Append(Row(header));
        foreach (var r in rows) sb.Append('\n').Append(Row(r));
        return sb.ToString();
    }

    static string Time(DateTimeOffset t) => t.ToLocalTime().ToString("HH:mm:ss", Inv);
    static string N(int v) => v.ToString("N0", Inv);
    static string N(double v) => v.ToString("N0", Inv);
    static string F1(double v) => v.ToString("N1", Inv);
    static string Pct(double fraction) => ((int)Math.Round(fraction * 100)).ToString(Inv) + "%";
}
