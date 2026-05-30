using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Veron;

static class ModelfileValidator
{
    public static bool IsValidName(string name) =>
        name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');

    static readonly HashSet<string> KnownParams = new()
    {
        "alias", "port", "context", "jinja", "flash_attention",
        "repeat_penalty", "n_gpu_layers", "batch_size", "wait"
    };

    static string ParameterExpectedType(string key) => key switch
    {
        "alias" => "string",
        "jinja" or "flash_attention" => "boolean",
        "repeat_penalty" => "float",
        "port" or "context" or "n_gpu_layers" or "batch_size" or "wait" => "integer",
        _ => "value"
    };

    static bool ValidateParameterValue(string key, string value)
    {
        return key switch
        {
            "alias" => true, // any string is valid
            "jinja" or "flash_attention" => bool.TryParse(value, out _),
            "repeat_penalty" => float.TryParse(value, out _),
            "port" or "context" or "n_gpu_layers" or "batch_size" or "wait" => int.TryParse(value, out _),
            _ => true // unknown keys already rejected before this point
        };
    }

    public static List<string> ValidateModelfile(string sourcePath, string name, string modelsDir)
    {
        var errors = new List<string>();

        // Check name validity
        if (!IsValidName(name))
        {
            errors.Add($"Error: invalid name \"{name}\": names must be alphanumeric, hyphens, or underscores");
            return errors; // no point checking further
        }

        // Read lines
        string[] lines;
        try
        {
            lines = File.ReadAllLines(sourcePath);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            errors.Add($"Error: cannot read modelfile: {ex.Message}");
            return errors;
        }

        // Check FROM directive exists
        string? from = null;
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
            {
                from = line[5..].Trim().Trim('"').Trim('\'');
                break;
            }
        }

        if (from is null)
        {
            errors.Add("Error: no FROM directive found in modelfile");
            return errors;
        }

        // Resolve model path (same logic as ParseModelfile)
        string? modelPath = null;

        if (from.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && File.Exists(from))
        {
            modelPath = from;
        }
        else if (from.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            string candidate = Path.Join(modelsDir, from);
            if (File.Exists(candidate))
                modelPath = candidate;
        }
        else if (File.Exists(Path.Join(modelsDir, from + ".gguf")))
        {
            modelPath = Path.Join(modelsDir, from + ".gguf");
        }

        if (modelPath is null)
        {
            errors.Add($"Error: model \"{from}\" not found in {modelsDir}");
            return errors;
        }

        // Validate PARAMETER directives (skip those inside TOOL blocks — they're validated separately)
        bool insideToolBlock = false;
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();

            // Track whether we're inside a TOOL block
            if (line.StartsWith("TOOL", StringComparison.OrdinalIgnoreCase))
            { insideToolBlock = true; continue; }
            if (line.StartsWith("END_TOOL", StringComparison.OrdinalIgnoreCase))
            { insideToolBlock = false; continue; }

            // Skip parameters that are inside TOOL blocks — handled by ValidateToolBlocks
            if (!insideToolBlock && line.StartsWith("PARAMETER", StringComparison.OrdinalIgnoreCase))
            {
                string rest = line[9..].Trim();
                int spaceIdx = rest.IndexOf(' ');
                if (spaceIdx < 0) continue;

                string key = rest[..spaceIdx].Trim().ToLowerInvariant();
                string value = rest[(spaceIdx + 1)..].Trim();

                // Strip quotes from value
                if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') ||
                                          (value[0] == '\'' && value[^1] == '\'')))
                    value = value[1..^1];

                // Check for unknown keys
                if (!KnownParams.Contains(key))
                {
                    errors.Add($"Error: unknown parameter key: \"{key}\" (did you mean one of: alias, port, context, jinja, flash_attention, repeat_penalty, n_gpu_layers, batch_size, wait?)");
                    return errors;
                }

                // Validate the value type matches what the parameter expects
                if (!ValidateParameterValue(key, value))
                {
                    string expectedType = ParameterExpectedType(key);
                    errors.Add($"Error: invalid parameter \"{key}\": \"{value}\" is not a valid {expectedType}");
                    return errors;
                }
            }
        }

        // Validate TOOL blocks
        var toolErrors = ClaudeCodeValidator.ValidateToolBlocks(sourcePath);
        errors.AddRange(toolErrors);

        return errors;
    }
}
