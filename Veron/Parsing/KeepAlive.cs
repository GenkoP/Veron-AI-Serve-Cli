using System;
using System.Globalization;
using System.Threading;

namespace Veron;

/// <summary>
/// Parses Ollama-style keep-alive durations: "5m", "30s", "1h", "300" (seconds),
/// "0" (unload right after use) and any negative value such as "-1" (never unload).
/// Never-unload is represented by <see cref="Timeout.InfiniteTimeSpan"/>.
/// </summary>
public static class KeepAlive
{
    public static readonly TimeSpan Default = TimeSpan.FromMinutes(5);

    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string s = text.Trim().ToLowerInvariant();
        double multiplier = 1; // seconds

        if (s.EndsWith("ms")) { multiplier = 0.001; s = s[..^2]; }
        else if (s.EndsWith('s')) { s = s[..^1]; }
        else if (s.EndsWith('m')) { multiplier = 60; s = s[..^1]; }
        else if (s.EndsWith('h')) { multiplier = 3600; s = s[..^1]; }

        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            return false;

        value = n < 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(n * multiplier);
        return true;
    }

    public static TimeSpan Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException("Invalid keep-alive duration: '" + text + "'");

    public static bool IsForever(TimeSpan value) => value == Timeout.InfiniteTimeSpan;

    /// <summary>Short human form: "forever", "0s", "30s", "5m", "1h", "1h30m".</summary>
    public static string Format(TimeSpan value)
    {
        if (IsForever(value)) return "forever";
        if (value < TimeSpan.FromMinutes(1) || value.Seconds != 0) return (int)value.TotalSeconds + "s";
        if (value < TimeSpan.FromHours(1) || value.Minutes != 0 && value.TotalHours < 1) return (int)value.TotalMinutes + "m";
        return value.Minutes == 0 ? (int)value.TotalHours + "h" : (int)value.TotalHours + "h" + value.Minutes + "m";
    }
}
