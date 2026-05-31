using System;
using System.Collections.Generic;

namespace Veron;

static class CopilotValidator
{
    static readonly Dictionary<string, HashSet<string>> KnownCopilotModes = new()
    {
        ["effort"]             = new() { "none", "low", "medium", "high", "xhigh", "max" },
        ["reasoning-effort"]   = new() { "none", "low", "medium", "high", "xhigh", "max" },
        ["mode"]               = new() { "interactive", "plan", "autopilot" },
        ["log-level"]          = new() { "none", "error", "warning", "info", "debug", "all", "default" },
        ["stream"]             = new() { "on", "off" },
        ["output-format"]      = new() { "text", "json" },
        ["bash-env"]           = new() { "on", "off" },
        ["mouse"]              = new() { "on", "off" },
    };

    static readonly HashSet<string> KnownCopilotIntParams = new()
    {
        "max-autopilot-continues"
    };

    public static List<string> ValidateCopilotParameter(string key, string? value)
    {
        if (value is null) return new(); // bare flag — nothing to validate

        var errors = new List<string>();

        // Check known enum parameters
        if (KnownCopilotModes.TryGetValue(key, out var validValues))
        {
            if (!validValues.Contains(value))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\". Valid values: {string.Join(", ", validValues)}");
                return errors;
            }
            return errors;
        }

        // Check known integer parameters
        if (KnownCopilotIntParams.Contains(key))
        {
            if (!int.TryParse(value, out _))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\" — expected an integer");
                return errors;
            }
            return errors;
        }

        // Unknown parameters pass through without validation
        return errors;
    }
}
