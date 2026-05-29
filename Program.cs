using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;

namespace Veron;

static class Program
{
    // ── Defaults ───────────────────────────────────────────────────────────
    const string DefaultModelsDir   = "/home/genkop/Workspace/llama-cpp/models";
    const string DefaultLlamaServer = "/home/genkop/Workspace/llama-cpp/llama.cpp/build/bin/llama-server";
    const string DefaultClaudeBin   = "claude";
    static readonly string VeronDir    = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".veron");
    static readonly string ModelfilesDir = Path.Join(VeronDir, "modelfiles");
    static readonly string PidFile       = Path.Join(VeronDir, "veron.pid");

    // ── Entry point ────────────────────────────────────────────────────────
    static void Main(string[] rawArgs)
    {
        if (rawArgs.Length == 0 || rawArgs[0] == "--help" || rawArgs[0] == "-h")
        { PrintUsage(); return; }

        string command = rawArgs[0];
        var opts       = ParseOpts(rawArgs.AsSpan(1));

        // Extract global options before dispatch
        string modelsDir = opts.GetValueOrDefault("models-dir") ?? ExpandEnv(DefaultModelsDir);

        switch (command)
        {
            case "ls":
            case "list":    CmdList(modelsDir); break;
            case "create":  CmdCreate(opts, modelsDir); break;
            case "h":
            case "help":    PrintUsage(); break;
            case "v":
            case "version": PrintVersion(); break;
            case "serve":   CmdServe(opts, modelsDir); break;
            case "claude":  CmdClaude(opts, modelsDir); break;
            case "stop":    CmdStop(); break;
            default:        PrintUsage(); Environment.Exit(1); break;
        }
    }

    // ── Commands ───────────────────────────────────────────────────────────

    static void CmdList(string modelsDir)
    {
        if (!Directory.Exists(ModelfilesDir))
        {
            Console.WriteLine("No modelfiles found in " + ModelfilesDir);
            return;
        }

        var files = Directory.GetFiles(ModelfilesDir, "*", SearchOption.TopDirectoryOnly)
                            .OrderBy(p => p).ToArray();
        if (files.Length == 0)
        {
            Console.WriteLine("No modelfiles found in " + ModelfilesDir);
            return;
        }

        // Parse each modelfile to show the FROM target
        var entries = new List<(string name, string fromTarget)>();
        foreach (var f in files)
        {
            string name = Path.GetFileName(f);
            string fromTarget = "";

            try
            {
                foreach (var line in File.ReadLines(f))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    if (trimmed.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                    {
                        fromTarget = trimmed[5..].Trim().Trim('"').Trim('\'');
                        break;
                    }
                }
            }
            catch { /* skip files we can't read */ }

            entries.Add((name, fromTarget));
        }

        int nameW = entries.Max(e => e.name.Length);
        int fromW = entries.Max(e => e.fromTarget.Length);
        string header = "NAME".PadLeft(nameW) + "  FROM";
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        foreach (var (name, fromTarget) in entries)
            Console.WriteLine(name.PadLeft(nameW) + "  " + fromTarget);

        Console.WriteLine();
        Console.WriteLine("Total: " + entries.Count + " modelfile(s)");
    }

    static void CmdServe(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = LoadConfig(opts, modelsDir);

        var cmd = BuildLlamaCmd(cfg);

        Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " …");
        Console.WriteLine("  Command: " + string.Join(" ", cmd.Select(EscapeArg)));

        var psi = ServerPsi(cmd);
        var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start llama-server");

        WritePid(proc.Id);
        string baseUrl = "http://localhost:" + cfg.Port;

        if (WaitForServer(baseUrl, cfg.Wait))
            Console.WriteLine("Server is ready at " + baseUrl);
        else
            Console.Error.WriteLine("Warning: server did not respond within " + cfg.Wait + "s");

        try { proc.WaitForExit(); }
        catch (OperationCanceledException) { } // Ctrl+C
        finally
        {
            if (!proc.HasExited)
            {
                Console.WriteLine("\nShutting down …");
                proc.Kill(true);
                Console.WriteLine("Stopped.");
            }
        }
    }

    static void CmdClaude(Dictionary<string, string> opts, string modelsDir)
    {
        var cfg = LoadConfig(opts, modelsDir);

        var cmd = BuildLlamaCmd(cfg);

        Console.WriteLine("Starting llama-server for " + cfg.Alias + " on port " + cfg.Port + " …");

        var psi = ServerPsi(cmd);
        var serverProc = Process.Start(psi)
                        ?? throw new InvalidOperationException("Failed to start llama-server");

        WritePid(serverProc.Id);
        string baseUrl = "http://localhost:" + cfg.Port;

        if (!WaitForServer(baseUrl, cfg.Wait))
        {
            Console.Error.WriteLine("Error: server did not respond within " + cfg.Wait + "s");
            Environment.Exit(1);
        }

        Console.WriteLine("Server is ready at " + baseUrl);

        // ── Launch claude code ────────────────────────────────────────────
        string claudeBin = opts.GetValueOrDefault("claude-bin", DefaultClaudeBin);
        var claudePsi = new ProcessStartInfo(claudeBin, "code")
        {
            UseShellExecute = false,
        };
        claudePsi.EnvironmentVariables["ANTHROPIC_BASE_URL"]           = baseUrl;
        claudePsi.EnvironmentVariables["CLAUDE_CODE_ATTRIBUTION_HEADER"] = "0";

        Console.WriteLine();
        Console.WriteLine("Launching claude code …");
        Console.WriteLine("  ANTHROPIC_BASE_URL             = " + baseUrl);
        Console.WriteLine("  CLAUDE_CODE_ATTRIBUTION_HEADER = 0");
        Console.WriteLine();

        try
        {
            var claudeProc = Process.Start(claudePsi)
                             ?? throw new FileNotFoundException(claudeBin);
            claudeProc.WaitForExit();
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is Win32Exception)
        {
            Console.Error.WriteLine("Error: couldn't launch '" + claudeBin + "' — make sure it's in $PATH.");
            Environment.Exit(1);
        }

        // ── Tear down the server ──────────────────────────────────────────
        Console.WriteLine("\nclaude code exited. Stopping llama-server …");
        if (!serverProc.HasExited)
            serverProc.Kill(true);
        DeletePid();
        Console.WriteLine("Done.");
    }

    static void CmdStop()
    {
        if (!ReadPid(out int pid))
        {
            Console.WriteLine("No saved PID — llama-server may not have been started by veron.");
            return;
        }

        if (!IsProcessAlive(pid))
        {
            Console.WriteLine("Process " + pid + " is no longer running. Cleaning up.");
            DeletePid();
            return;
        }

        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill(true);
            Console.WriteLine("Stopped llama-server (PID " + pid + ").");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("Process " + pid + " already gone.");
        }
        finally { DeletePid(); }
    }

    static void CmdCreate(Dictionary<string, string> opts, string modelsDir)
    {
        string? name = opts.GetValueOrDefault("model");
        string? sourcePath = opts.GetValueOrDefault("source-path");

        if (name is null || sourcePath is null)
        {
            Console.Error.WriteLine("Error: 'create' requires <name> and <path-to-modelfile>");
            Console.Error.WriteLine("Usage: veron create <name> <path-to-modelfile>");
            Environment.Exit(1);
        }

        // Source file must exist
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Error: source modelfile not found: {sourcePath}");
            Environment.Exit(1);
        }

        // Validate
        var errors = ValidateModelfile(sourcePath, name, modelsDir);
        if (errors.Count > 0)
        {
            foreach (var err in errors)
                Console.Error.WriteLine(err);
            Environment.Exit(1);
        }

        // Ensure destination directory exists
        Directory.CreateDirectory(ModelfilesDir);

        // Copy (overwrite if exists — allows updates)
        string destPath = Path.Join(ModelfilesDir, name);
        File.Copy(sourcePath, destPath, overwrite: true);

        Console.WriteLine($"Using modelfile: {sourcePath}");
        Console.WriteLine($"Creating profile \"{name}\" -> {destPath} ✓");
    }

    // ── Modelfile / Config loading ─────────────────────────────────────────

    /// <summary>
    /// Resolves the modelfile name, parses it, then overlays CLI flags on top.
    /// </summary>
    static ModelConfig LoadConfig(Dictionary<string, string> opts, string modelsDir)
    {
        string raw = opts.GetValueOrDefault("model")
                ?? throw new ArgumentNullException("model argument is required for serve/claude");

        // ── Step 1: find the modelfile ─────────────────────────────────────
        string? mfPath = FindModelfile(raw);

        if (mfPath is null)
            throw new FileNotFoundException("No modelfile found for '" + raw + "' in " + ModelfilesDir);

        Console.WriteLine("Using modelfile: " + mfPath);

        // ── Step 2: parse it ───────────────────────────────────────────────
        var cfg = ParseModelfile(mfPath, modelsDir);

        // ── Step 3: overlay CLI flags (command-line always wins) ───────────
        if (opts.GetValueOrDefault("alias") is string a)   cfg.Alias = a;
        if (opts.TryGetValue("port", out var v))           cfg.Port    = int.Parse(v);
        if (opts.TryGetValue("context", out v))            cfg.Context = int.Parse(v);

        // --no-jinja overrides; --jinja is default so no flag needed to set true
        if (OptsBool(opts, "no-jinja"))                    cfg.Jinja   = false;
        if (OptsBool(opts, "no-flash-attention"))          cfg.Fa      = false;

        if (opts.TryGetValue("repeat-penalty", out v))     cfg.RepeatPenalty = float.Parse(v);
        if (opts.TryGetValue("n-gpu-layers", out v))       cfg.NGpuLayers    = int.Parse(v);
        if (opts.TryGetValue("batch-size", out v))         cfg.BatchSize     = int.Parse(v);
        if (opts.TryGetValue("wait", out v))               cfg.Wait          = int.Parse(v);

        return cfg;
    }

    /// <summary>
    /// Finds a modelfile in ModelfilesDir matching the given name.
    /// Tries exact filename first, then strips extension and rechecks,
    /// then tries appending .modelfile.
    /// </summary>
    static string? FindModelfile(string name)
    {
        if (!Directory.Exists(ModelfilesDir)) return null;

        // Exact match (e.g. user types "MiniCPM5-1B-Q4_K_M.modelfile")
        string exact = Path.Join(ModelfilesDir, name);
        if (File.Exists(exact)) return exact;

        // Name without extension (user types "MiniCPM5-1B-Q4_K_M", file could be "MiniCPM5-1B-Q4_K_M" or "MiniCPM5-1B-Q4_K_M.modelfile" or anything)
        string stem = Path.GetFileNameWithoutExtension(name);

        // List all files in modelfiles dir and match by stem
        var files = Directory.GetFiles(ModelfilesDir, "*", SearchOption.TopDirectoryOnly);
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

    static ModelConfig ParseModelfile(string path, string modelsDir)
    {
        var lines    = File.ReadAllLines(path);
        string? from = null;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue; // skip blanks / comments

            // ── FROM directive ─────────────────────────────────────────────
            if (line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
            {
                from = line[5..].Trim().Trim('"').Trim('\'');
                continue;
            }

            // ── PARAMETER directive ────────────────────────────────────────
            if (line.StartsWith("PARAMETER", StringComparison.OrdinalIgnoreCase))
            {
                // We'll apply these after we know the model path — skip here
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

        // ── Build config with modelfile defaults ──────────────────────────
        var cfg = new ModelConfig
        {
            ModelPath = modelPath,
            Alias     = baseAlias,
            Port      = 5570,
            Context   = 128000,
            Jinja     = true,
            Fa        = true,
            RepeatPenalty = 1.05f,
            Wait      = 30,
        };

        // ── Apply PARAMETER directives ─────────────────────────────────────
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
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

    static void ApplyParameter(ModelConfig cfg, string key, string value)
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
        }
    }

    static bool IsValidName(string name) =>
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

    static List<string> ValidateModelfile(string sourcePath, string name, string modelsDir)
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

        // Validate PARAMETER directives
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("PARAMETER", StringComparison.OrdinalIgnoreCase)) continue;

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

        return errors;
    }

    // ── ModelConfig — resolved config after modelfile + CLI overlay ────────

    class ModelConfig
    {
        public string  ModelPath     { get; set; } = "";
        public string  Alias         { get; set; } = "";
        public int     Port          { get; set; } = 5570;
        public int     Context       { get; set; } = 128000;
        public bool    Jinja         { get; set; } = true;
        public bool    Fa            { get; set; } = true;
        public float   RepeatPenalty { get; set; } = 1.05f;
        public int?    NGpuLayers    { get; set; }
        public int?    BatchSize     { get; set; }
        public int     Wait          { get; set; } = 30;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static List<string> BuildLlamaCmd(ModelConfig cfg)
    {
        var cmd = new List<string>
        {
            DefaultLlamaServer,
            "-m", cfg.ModelPath,
            "--alias", cfg.Alias,
            "--port", cfg.Port.ToString(),
            "-c", cfg.Context.ToString(),
            "-fa", cfg.Fa ? "1" : "0",
            "--repeat-penalty", cfg.RepeatPenalty.ToString("0.00"),
        };

        if (cfg.Jinja) cmd.Add("--jinja");

        if (cfg.NGpuLayers.HasValue) { cmd.Add("-ngl"); cmd.Add(cfg.NGpuLayers.Value.ToString()); }
        if (cfg.BatchSize.HasValue)  { cmd.Add("-b");   cmd.Add(cfg.BatchSize.Value.ToString()); }

        return cmd;
    }

    static ProcessStartInfo ServerPsi(List<string> cmd)
    {
        return new ProcessStartInfo(cmd[0], string.Join(" ", cmd.Skip(1).Select(EscapeArg)))
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
    }

    static bool WaitForServer(string baseUrl, int timeoutSec)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var r = http.GetAsync(baseUrl + "/health").GetAwaiter().GetResult();
                if (r.IsSuccessStatusCode) return true;
            }
            catch { /* not ready yet */ }
            Task.Delay(1000).GetAwaiter().GetResult();
        }
        return false;
    }

    static void WritePid(int pid)
    {
        Directory.CreateDirectory(VeronDir);
        File.WriteAllText(PidFile, pid.ToString());
    }

    static bool ReadPid(out int pid)
    {
        if (File.Exists(PidFile))
        {
            string text = File.ReadAllText(PidFile).Trim();
            if (int.TryParse(text, out int p)) { pid = p; return true; }
        }
        pid = 0;
        return false;
    }

    static void DeletePid() => File.Delete(PidFile);

    static bool IsProcessAlive(int pid)
    {
        try { Process.GetProcessById(pid); return true; }
        catch { return false; }
    }

    // ── Argument parsing ───────────────────────────────────────────────────

    static Dictionary<string, string> ParseOpts(ReadOnlySpan<string> args)
    {
        var dict = new Dictionary<string, string>();
        bool gotModel = false;
        bool gotSourcePath = false;
        for (int i = 0; i < args.Length; i++)
        {
            // First positional arg = model name
            if (!gotModel && !args[i].StartsWith("-"))
            {
                dict["model"] = args[i];
                gotModel = true;
                continue;
            }

            // Second positional arg = source path (used by 'create')
            if (!gotSourcePath && !args[i].StartsWith("-"))
            {
                dict["source-path"] = args[i];
                gotSourcePath = true;
                continue;
            }

            if (!args[i].StartsWith("--")) continue;

            string key = args[i][2..];

            // --key=value
            int eqPos = key.IndexOf('=');
            if (eqPos >= 0) { dict[key[..eqPos]] = key[(eqPos + 1)..]; continue; }

            // --key value  OR  --key (boolean true)
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                { i++; dict[key] = args[i]; }
            else dict[key] = "true";
        }
        return dict;
    }

    // ── Option helpers ─────────────────────────────────────────────────────

    static bool OptsBool(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out var v) && v == "true";

    static string ExpandEnv(string path) =>
        path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     StringComparison.Ordinal);

    static string EscapeArg(string arg) => arg.Contains(' ') ? "\"" + arg + "\"" : arg;

    // ── Usage ──────────────────────────────────────────────────────────────

    static void PrintUsage()
    {
        Console.WriteLine(@"
Veron — manage llama.cpp models and launch claude code against them.

USAGE
  veron <command> [options]

COMMANDS
  ls, list            List all available modelfiles
  create <name> <path> Create a profile from a modelfile (validates first)
  serve <name>        Start llama-server with the given model profile (foreground)
  claude <name>       Start llama-server then launch claude code (auto-stop after)
  stop                Stop a previously started llama-server
  h, help             Show this help message
  v, version          Show version information

GLOBAL OPTIONS
  --models-dir <dir>   Directory containing GGUF files

SERVE / CLAUDE OPTIONS
  <name>               Modelfile name (without extension) in ~/.veron/modelfiles/
  --alias <name>       Alias for the server  (overwrites modelfile)
  --port <n>           Port  (default: 5570)
  --context <n>        Context size  (default: 128000)
  --jinja              Use Jinja template  (default: on)
  --no-jinja           Disable Jinja template
  --flash-attention    Enable flash attention  (default: on)
  --no-flash-attention Disable flash attention
  --repeat-penalty <f> Repeat penalty  (default: 1.05)
  --n-gpu-layers <n>   GPU layers to offload
  --batch-size <n>     Batch size
  --wait <n>           Seconds to wait for server readiness  (default: 30)

MODELFILeS
  Place modelfile files in ~/.veron/modelfiles/ — no extension required.

  Modelfile format (inspired by Ollama):

    FROM Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf
    PARAMETER alias ""Qwopus3.6-27b-MTP""
    PARAMETER port 5570
    PARAMETER context 128000
    PARAMETER jinja true
    PARAMETER flash_attention true
    PARAMETER repeat_penalty 1.05
    PARAMETER n_gpu_layers 99
    PARAMETER batch_size 512
    PARAMETER wait 30

  You can create multiple profiles for the same GGUF model, e.g.:
    ~/.veron/modelfiles/my-model-small   (context 32000)
    ~/.veron/modelfiles/my-model-large   (context 128000)

  Command-line flags always override modelfile values.

EXAMPLES
  veron ls
  veron serve my-model-small
  veron claude my-model-large --port 5571
  veron serve Qwopus3.6-27b-MTP

ENVIRONMENT (set automatically by 'claude')
  ANTHROPIC_BASE_URL          = http://localhost:<port>
  CLAUDE_CODE_ATTRIBUTION_HEADER = 0".TrimStart());
    }

    // ── Version ─────────────────────────────────────────────────────────────

    static void PrintVersion()
    {
        Console.WriteLine("veron 1.0.0");
    }
}
