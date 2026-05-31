using System;
using System.Collections.Generic;

namespace Veron;

static class ClaudeCodeValidator
{
    static readonly Dictionary<string, HashSet<string>> KnownClaudeCodeModes = new()
    {
        ["permission-mode"] = new() { "auto", "plan", "dontAsk", "bypassPermissions", "default", "acceptEdits" },
        ["effort"] = new() { "low", "medium", "high", "xhigh", "max" }
    };

    static readonly HashSet<string> KnownClaudeCodeIntParams = new()
    {
        "max-turns"
    };

    static readonly HashSet<string> KnownClaudeCodeFloatParams = new()
    {
        "max-budget-usd"
    };

    public static List<string> ValidateClaudeCodeParameter(string key, string? value)
    {
        if (value is null) return new(); // bare flag — nothing to validate

        var errors = new List<string>();

        // Check if this is a known parameter with specific valid values
        if (KnownClaudeCodeModes.TryGetValue(key, out var validValues))
        {
            if (!validValues.Contains(value))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\". Valid values: {string.Join(", ", validValues)}");
                return errors;
            }
            return errors;
        }

        // Check if this is a known integer parameter
        if (KnownClaudeCodeIntParams.Contains(key))
        {
            if (!int.TryParse(value, out _))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\" — expected an integer");
                return errors;
            }
            return errors;
        }

        // Check if this is a known float parameter
        if (KnownClaudeCodeFloatParams.Contains(key))
        {
            if (!float.TryParse(value, out _))
            {
                errors.Add($"Error: invalid value \"{value}\" for tool parameter \"{key}\" — expected a number");
                return errors;
            }
            return errors;
        }

        // Unknown parameters pass through without validation
        return errors;
    }

    public static List<string> ValidateToolBlocks(string sourcePath)
    {
        var errors = new List<string>();

        // Parse TOOL blocks — this will throw on structural errors (END_TOOL without TOOL, etc.)
        Dictionary<string, ToolConfig>? toolConfigs;
        try
        {
            toolConfigs = ModelfileParser.ParseToolBlocks(sourcePath);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
            return errors;
        }

        // Validate parameters in each TOOL block
        foreach (var (_, toolCfg) in toolConfigs)
        {
            foreach (var (key, value) in toolCfg.Parameters)
            {
                var paramErrors = ValidateClaudeCodeParameter(key, value);
                if (paramErrors.Count > 0)
                {
                    errors.Add($"Error in TOOL \"{toolCfg.Name}\": {paramErrors[0]}");
                    return errors;
                }
            }
        }

        return errors;
    }
}
