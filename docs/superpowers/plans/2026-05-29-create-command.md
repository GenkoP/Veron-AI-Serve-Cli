# Veron `create` Command — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `veron create <name> <path>` command that copies and validates user modelfiles into `~/.veron/modelfiles/`.

**Architecture:** Extend `ParseOpts` to handle a second positional arg, add `ValidateModelfile` helper wrapping existing `ParseModelfile` logic with formatted errors, add `CmdCreate` handler, wire into switch dispatch and usage text. All changes are in `Program.cs`.

**Tech Stack:** .NET 10, C# (single-file CLI)

---

### File Map

| Action | File | What changes |
|--------|------|--------------|
| Modify | `Program.cs:501-528` | Extend `ParseOpts` for second positional arg |
| Modify | `Program.cs:409-423` | Add `ValidParameterKeys` to `ModelConfig` region |
| Modify | `Program.cs:393-407` | Add unknown-key tracking to `ApplyParameter` |
| Modify | `Program.cs:new after line 407` | Add `ValidateModelfile`, `IsValidName`, `IsNameValid` helpers |
| Modify | `Program.cs:new after line 219` | Add `CmdCreate` command handler |
| Modify | `Program.cs:31-43` | Wire `create` into switch dispatch |
| Modify | `Program.cs:544-608` | Update `PrintUsage` with create command docs |

---

### Task 1: Extend ParseOpts for Second Positional Argument

**Files:**
- Modify: `Program.cs:501-528`

The current `ParseOpts` captures only one positional arg (as `"model"`). For `create`, we need two: `<name>` and `<path-to-modelfile>`. The second positional is captured as `"source-path"`.

- [ ] **Step 1: Modify ParseOpts to capture a second positional**

In `Program.cs` at line 501, change the method body:

```csharp
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
```

- [ ] **Step 2: Build to verify no compile errors**

Run: `dotnet build`
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Program.cs
git commit -m "feat(create): extend ParseOpts to capture second positional arg as source-path"
```

---

### Task 2: Add Name Validation and ValidateModelfile Helper

**Files:**
- Modify: `Program.cs` — add new methods after `ApplyParameter` (after line 407)

Two pieces: a simple name-check method, and the main validation wrapper.

- [ ] **Step 1: Add IsValidName helper**

Add this static method after the `ApplyParameter` method (around line 408):

```csharp
static bool IsValidName(string name) =>
    name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
```

- [ ] **Step 2: Add ValidateModelfile helper**

This method reads the modelfile, checks structure, resolves the FROM path, and validates parameters — returning a list of error strings (empty = valid). It reuses `ParseModelfile`'s resolution logic via `ApplyParameter`.

Add after `IsValidName`:

```csharp
static readonly HashSet<string> KnownParams = new()
{
    "alias", "port", "context", "jinja", "flash_attention",
    "repeat_penalty", "n_gpu_layers", "batch_size", "wait"
};

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

        // Try to apply the parameter — catches invalid values
        var testCfg = new ModelConfig();
        try
        {
            ApplyParameter(testCfg, key, value);
        }
        catch (FormatException)
        {
            errors.Add($"Error: invalid parameter \"{key}\": \"{value}\" is not a valid value for {key}");
            return errors;
        }
        catch (OverflowException)
        {
            errors.Add($"Error: invalid parameter \"{key}\": \"{value}\" is out of range");
            return errors;
        }
    }

    return errors;
}
```

- [ ] **Step 3: Build to verify no compile errors**

Run: `dotnet build`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Program.cs
git commit -m "feat(create): add IsValidName and ValidateModelfile helpers with full validation"
```

---

### Task 3: Add CmdCreate Command Handler

**Files:**
- Modify: `Program.cs` — add after `CmdStop` (after line 219)

- [ ] **Step 1: Write CmdCreate method**

Add this method after `CmdStop`:

```csharp
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
```

- [ ] **Step 2: Build to verify no compile errors**

Run: `dotnet build`
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Program.cs
git commit -m "feat(create): add CmdCreate command handler with copy and validation flow"
```

---

### Task 4: Wire into Switch Dispatch and Update Usage

**Files:**
- Modify: `Program.cs:31-43` (switch dispatch)
- Modify: `Program.cs:544-608` (PrintUsage)

- [ ] **Step 1: Add "create" case to switch dispatch**

In the switch statement at line 31, add a case for `"create"` between `"list"` and `"h"`/`"help"`:

```csharp
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
```

- [ ] **Step 2: Update PrintUsage**

In the `PrintUsage` method, add `create` to the COMMANDS section and add a CREATE OPTIONS section. Change this part of the usage text:

Current (lines 552-558):
```csharp
COMMANDS
  ls, list            List all available modelfiles
  serve <name>        Start llama-server with the given model profile (foreground)
  claude <name>       Start llama-server then launch claude code (auto-stop after)
  stop                Stop a previously started llama-server
  h, help             Show this help message
  v, version          Show version information
```

Change to:
```csharp
COMMANDS
  ls, list            List all available modelfiles
  create <name> <path> Create a profile from a modelfile (validates first)
  serve <name>        Start llama-server with the given model profile (foreground)
  claude <name>       Start llama-server then launch claude code (auto-stop after)
  stop                Stop a previously started llama-server
  h, help             Show this help message
  v, version          Show version information
```

- [ ] **Step 3: Build to verify no compile errors**

Run: `dotnet build`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Program.cs
git commit -m "feat(create): wire create command into switch dispatch and update usage text"
```

---

### Task 5: Manual Testing

**Files:** none — verification only

- [ ] **Step 1: Test successful creation**

Create a test modelfile at `/tmp/test-model.modelfile`:
```bash
cat > /tmp/test-model.modelfile << 'EOF'
FROM MiniCPM5-1B-Q4_K_M.gguf

PARAMETER alias "TestModel"
PARAMETER port 5580
PARAMETER context 32000
PARAMETER jinja true
PARAMETER flash_attention true
PARAMETER repeat_penalty 1.05
EOF
```

Run: `dotnet run -- create test-profile /tmp/test-model.modelfile`
Expected output:
```
Using modelfile: /tmp/test-model.modelfile
Creating profile "test-profile" -> ~/.veron/modelfiles/test-profile ✓
```

- [ ] **Step 2: Verify it appears in veron ls**

Run: `dotnet run -- ls`
Expected: `test-profile` and `MiniCPM5-1B-Q4_K_M.gguf` appear in the listing.

- [ ] **Step 3: Test overwrite (update scenario)**

Modify `/tmp/test-model.modelfile` to change the alias, then run create again:
```bash
dotnet run -- create test-profile /tmp/test-model.modelfile
```
Expected: Same success message — no error about existing file.

- [ ] **Step 4: Test invalid name**

Run: `dotnet run -- create "foo/bar" /tmp/test-model.modelfile`
Expected: Error about invalid name, exit code non-zero.

- [ ] **Step 5: Test missing source file**

Run: `dotnet run -- create some-name /nonexistent/file.modelfile`
Expected: `Error: source modelfile not found: /nonexistent/file.modelfile`, exit code non-zero.

- [ ] **Step 6: Test missing FROM directive**

Create `/tmp/bad-modelfile.modelfile`:
```bash
echo 'PARAMETER alias "Bad"' > /tmp/bad-modelfile.modelfile
```

Run: `dotnet run -- create bad-test /tmp/bad-modelfile.modelfile`
Expected: `Error: no FROM directive found in modelfile`, exit code non-zero.

- [ ] **Step 7: Test unknown parameter key**

Create `/tmp/unknown-param.modelfile`:
```bash
cat > /tmp/unknown-param.modelfile << 'EOF'
FROM MiniCPM5-1B-Q4_K_M.gguf
PARAMETER foobar 123
EOF
```

Run: `dotnet run -- create unknown-test /tmp/unknown-param.modelfile`
Expected: Error about unknown parameter key "foobar" with suggestion list, exit code non-zero.

- [ ] **Step 8: Test invalid parameter value**

Create `/tmp/bad-value.modelfile`:
```bash
cat > /tmp/bad-value.modelfile << 'EOF'
FROM MiniCPM5-1B-Q4_K_M.gguf
PARAMETER port abc
EOF
```

Run: `dotnet run -- create bad-val-test /tmp/bad-value.modelfile`
Expected: Error about invalid parameter "port": "abc" is not a valid value.

- [ ] **Step 9: Test missing model file**

Create `/tmp/missing-model.modelfile`:
```bash
cat > /tmp/missing-model.modelfile << 'EOF'
FROM NonExistentModel.gguf
EOF
```

Run: `dotnet run -- create missing-test /tmp/missing-model.modelfile`
Expected: Error about model "NonExistentModel.gguf" not found in models dir.

- [ ] **Step 10: Test help text includes create**

Run: `dotnet run -- help`
Expected: `create <name> <path>` appears in the COMMANDS section of help output.

---

### Task 6: Clean Build and Final Commit

**Files:** none — finalization

- [ ] **Step 1: Clean release build**

Run: `dotnet publish -c Release -o bin/release`
Expected: Builds successfully with 0 errors and 0 warnings (or only pre-existing warnings).

- [ ] **Step 2: Final commit**

```bash
git add Program.cs
git commit -m "feat(create): complete veron create command — validate and copy user modelfiles"
```

---

## Self-Review Checklist

1. **Spec coverage:**
   - Command signature `veron create <name> <path>` ✓ (Task 3, Step 1)
   - Source file exists check ✓ (Task 3, Step 1 — CmdCreate)
   - Name sanitization ✓ (Task 2, Step 1 — IsValidName; Task 2, Step 2 — ValidateModelfile)
   - FROM directive present ✓ (Task 2, Step 2 — ValidateModelfile)
   - Model file exists on disk ✓ (Task 2, Step 2 — ValidateModelfile)
   - Parameters valid ✓ (Task 2, Step 2 — ValidateModelfile via ApplyParameter)
   - Unknown parameter keys rejected ✓ (Task 2, Step 2 — KnownParams check)
   - Overwrite existing files silently ✓ (Task 3, Step 1 — File.Copy with overwrite:true)
   - Success output matches spec ✓ (Task 3, Step 1 — Console.WriteLine messages)
   - Non-zero exit on validation failure ✓ (Task 3, Step 1 — Environment.Exit(1))
   - `veron ls` shows new profile ✓ (no code change needed — CmdList reads all files in ModelfilesDir)

2. **Placeholder scan:** No TBDs, TODOs, or vague instructions found. All code blocks are complete.

3. **Type consistency:**
   - `KnownParams` uses same key names as `ApplyParameter` switch: alias, port, context, jinja, flash_attention, repeat_penalty, n_gpu_layers, batch_size, wait ✓
   - `ValidateModelfile` returns `List<string>` matching the error message format in spec ✓
   - `ModelConfig` properties unchanged — no breaking changes to existing code ✓
   - `ParseOpts` second positional key is `"source-path"` — not used by existing commands ✓
