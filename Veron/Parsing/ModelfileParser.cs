using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Veron;

static class ModelfileParser
{
    /// <summary>
    /// Finds a modelfile in Paths.ModelfilesDir matching the given name.
    /// Tries exact filename first, then strips extension and rechecks,
    /// then tries appending .modelfile.
    /// </summary>
    public static string? FindModelfile(string name) => FindModelfile(name, Paths.ModelfilesDir);

    public static string? FindModelfile(string name, string modelfilesDir)
    {
        if (!Directory.Exists(modelfilesDir)) return null;
        if (name.Contains('/') || name.Contains('\\') || name is "." or "..") return null;

        // Exact match (e.g. user types "MiniCPM5-1B-Q4_K_M.modelfile")
        string exact = Path.Join(modelfilesDir, name);
        if (File.Exists(exact)) return exact;

        // Name without extension (user types "MiniCPM5-1B-Q4_K_M", file could be "MiniCPM5-1B-Q4_K_M" or "MiniCPM5-1B-Q4_K_M.modelfile" or anything)
        string stem = Path.GetFileNameWithoutExtension(name);

        // List all files in modelfiles dir and match by stem
        var files = Directory.GetFiles(modelfilesDir, "*", SearchOption.TopDirectoryOnly);
        foreach (var f in files)
        {
            string fname = Path.GetFileName(f);
            if (fname.Equals(stem, StringComparison.OrdinalIgnoreCase)) return f;

            // Also check: does the filename start with stem + "." ?
            // e.g. stem="MiniCPM5-1B" and file is "MiniCPM5-1B.modelfile" or "MiniCPM5-1B.txt"
            if (fname.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)) return f;
        }

        return null;
    }

    public static ModelConfig ParseModelfile(string path, string modelsDir)
    {
        var lines    = File.ReadAllLines(path);
        string? from = null;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue; // skip blanks / comments

            // FROM directive
            if (line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
            {
                from = line[5..].Trim().Trim('"').Trim('\'');
                continue;
            }

            // PARAMETER directive — skip, applied later
            if (line.StartsWith("PARAMETER", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
        }

        // Resolve FROM into actual model path
        string? modelPath = null;
        string baseAlias  = "";

        if (from is not null)
        {
            if (from.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && File.Exists(from))
            {
                // Absolute path to .gguf
                modelPath = from;
                baseAlias = Path.GetFileNameWithoutExtension(from);
            }
            else if (from.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            {
                // Relative .gguf — look under modelsDir
                string candidate = Path.Join(modelsDir, from);
                if (File.Exists(candidate))
                {
                    modelPath = candidate;
                    baseAlias = Path.GetFileNameWithoutExtension(from);
                }
            }
            else if (File.Exists(Path.Join(modelsDir, from + ".gguf")))
            {
                // No extension — append .gguf under modelsDir
                modelPath = Path.Join(modelsDir, from + ".gguf");
                baseAlias = from;
            }
        }

        if (modelPath is null && from is not null)
            throw new FileNotFoundException("Model '" + from + "' not found in " + modelsDir);

        // Fallback: if no FROM, use the modelfile name itself as model
        if (modelPath is null)
        {
            string mfName = Path.GetFileNameWithoutExtension(path);
            modelPath = Path.Join(modelsDir, mfName + ".gguf");
            baseAlias = mfName;
        }

        // Build config with modelfile defaults
        var cfg = new ModelConfig
        {
            ModelPath = modelPath,
            Alias     = baseAlias,
            Port      = 5570,
            Context   = 128000,
            Jinja     = true,
            Fa        = true,
            RepeatPenalty = 1.05f,
            Wait      = 120,
        };

        // Apply top-level PARAMETER directives (those inside TOOL blocks belong to the tool)
        bool insideTool = false;
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("END_TOOL", StringComparison.OrdinalIgnoreCase)) { insideTool = false; continue; }
            if (line.StartsWith("TOOL", StringComparison.OrdinalIgnoreCase))     { insideTool = true;  continue; }
            if (insideTool) continue;
            if (!line.StartsWith("PARAMETER", StringComparison.OrdinalIgnoreCase)) continue;

            string rest = line[9..].Trim(); // everything after "PARAMETER"
            int spaceIdx = rest.IndexOf(' ');
            if (spaceIdx < 0) continue;

            string key   = rest[..spaceIdx].Trim().ToLowerInvariant();
            string value = rest[(spaceIdx + 1)..].Trim();

            // Strip quotes
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') ||
                                      (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            ApplyParameter(cfg, key, value);
        }

        return cfg;
    }

    public static Dictionary<string, ToolConfig> ParseToolBlocks(string path)
    {
        var result = new Dictionary<string, ToolConfig>();
        var lines   = File.ReadAllLines(path);

        string? currentToolName = null;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            // Start of a TOOL block
            if (line.StartsWith("TOOL", StringComparison.OrdinalIgnoreCase))
            {
                if (currentToolName is not null)
                    throw new InvalidOperationException("Nested TOOL blocks are not allowed: '" + currentToolName + "' is already open");

                string toolName = line[4..].Trim();
                if (toolName.Length == 0)
                    throw new InvalidOperationException("'TOOL' directive requires a tool name");

                currentToolName = toolName;
                result[currentToolName] = new ToolConfig { Name = currentToolName };
                continue;
            }

            // End of a TOOL block
            if (line.StartsWith("END_TOOL", StringComparison.OrdinalIgnoreCase))
            {
                if (currentToolName is null)
                    throw new InvalidOperationException("'END_TOOL' without matching 'TOOL' directive");

                currentToolName = null;
                continue;
            }

            // PARAMETER inside a TOOL block
            if (currentToolName is not null && line.StartsWith("PARAMETER", StringComparison.OrdinalIgnoreCase))
            {
                string rest = line[9..].Trim();
                int spaceIdx = rest.IndexOf(' ');
                if (spaceIdx < 0)
                {
                    string bareKey = rest.Trim();
                    result[currentToolName].Parameters[bareKey] = null; // bare flag
                    continue;
                }

                string key   = rest[..spaceIdx].Trim();
                string value = rest[(spaceIdx + 1)..].Trim();

                if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') ||
                                          (value[0] == '\'' && value[^1] == '\'')))
                    value = value[1..^1];

                result[currentToolName].Parameters[key] = value;
                continue;
            }
        }

        if (currentToolName is not null)
            throw new InvalidOperationException($"Unclosed TOOL block for '{currentToolName}' — missing END_TOOL");

        return result;
    }

    public static void ApplyParameter(ModelConfig cfg, string key, string value)
    {
        switch (key)
        {
            case "alias":           cfg.Alias           = value; break;
            case "port":            if (int.TryParse(value, out int p))     cfg.Port      = p; break;
            case "context":         if (int.TryParse(value, out int c))     cfg.Context   = c; break;
            case "jinja":           cfg.Jinja           = bool.Parse(value); break;
            case "flash_attention": cfg.Fa              = bool.Parse(value); break;
            case "repeat_penalty":  if (float.TryParse(value, out float r)) cfg.RepeatPenalty = r; break;
            case "n_gpu_layers":    if (int.TryParse(value, out int n))     cfg.NGpuLayers    = n; break;
            case "batch_size":      if (int.TryParse(value, out int b))     cfg.BatchSize     = b; break;
            case "wait":            if (int.TryParse(value, out int w))     cfg.Wait          = w; break;
            case "keep_alive":      if (KeepAlive.TryParse(value, out var k)) cfg.KeepAlive   = k; break;
            case "args":            cfg.ExtraArgs.AddRange(ArgSplitter.Split(value)); break; // multiple lines accumulate
        }
    }
}
