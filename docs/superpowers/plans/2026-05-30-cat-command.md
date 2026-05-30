# Cat Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `veron cat <name>` to display raw modelfile content.

**Architecture:** New `CmdCat` static class in `Veron/Commands/CmdCat.cs` that resolves the modelfile via `ModelfileParser.FindModelfile(name)`, then prints its contents. Wires into the existing command dispatch in `Program.cs`.

**Tech Stack:** .NET 8, C#, xUnit

---

### File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `Veron/Commands/CmdCat.cs` | Resolve modelfile and print raw content |
| Modify | `Veron/Program.cs:38-54` | Wire up `cat` in switch dispatch |
| Modify | `Veron/Program.cs:60-166` | Add `cat` to help text |
| Create | `tests/CatCommandTests.cs` | Tests for cat command behavior |

---

### Task 1: Write Tests for CmdCat

**Files:**
- Create: `tests/CatCommandTests.cs`

- [ ] **Step 1: Create the test file with three tests**

```csharp
using System;
using System.IO;
using Veron;
using Xunit;

namespace Veron.Tests;

public class CatCommandTests
{
    [Fact]
    public void CmdCat_Preserves_Modelfile_Content()
    {
        // Arrange — create a modelfile in ~/.veron/modelfiles/
        string testName = "cat-test-" + Guid.NewGuid().ToString("N")[..8];
        string content = $"FROM model-{testName}.gguf\nPARAMETER port 5570";
        string mfPath = Path.Join(Paths.ModelfilesDir, testName);

        try
        {
            Directory.CreateDirectory(Paths.ModelfilesDir);
            File.WriteAllText(mfPath, content);

            // Act — capture stdout
            using var sw = new StringWriter();
            Console.SetOut(sw);
            CmdCat.Run(testName);

            // Assert
            string output = sw.ToString();
            Assert.Equal(content, output);
        }
        finally
        {
            // Cleanup
            File.Delete(mfPath);
        }
    }

    [Fact]
    public void CmdCat_Exit1_For_Nonexistent_Modelfile()
    {
        string testName = "cat-test-nonexistent-" + Guid.NewGuid().ToString("N")[..8];

        // Act & Assert — should call Environment.Exit(1)
        var ex = Assert.ThrowsAny<Environment.ExitException>(() => CmdCat.Run(testName));
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void CmdCat_Resolved_By_Stem_Name()
    {
        // Arrange — modelfile has extension, cat uses stem name
        string testName = "cat-stem-test-" + Guid.NewGuid().ToString("N")[..8];
        string content = $"FROM model-{testName}.gguf";
        string mfPath = Path.Join(Paths.ModelfilesDir, testName + ".modelfile");

        try
        {
            Directory.CreateDirectory(Paths.ModelfilesDir);
            File.WriteAllText(mfPath, content);

            // Act — resolve by stem (no .modelfile extension)
            using var sw = new StringWriter();
            Console.SetOut(sw);
            CmdCat.Run(testName);

            // Assert
            string output = sw.ToString();
            Assert.Equal(content, output);
        }
        finally
        {
            File.Delete(mfPath);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (CmdCat doesn't exist yet)**

Run: `dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~CatCommandTests" -v normal`

Expected: Build failure — `CmdCat` type not found.

- [ ] **Step 3: Commit the failing tests**

```bash
git add tests/CatCommandTests.cs
git commit -m "test: add CmdCat tests (failing)"
```

---

### Task 2: Implement CmdCat

**Files:**
- Create: `Veron/Commands/CmdCat.cs`

- [ ] **Step 1: Write the CmdCat class**

```csharp
using System;
using System.IO;

namespace Veron;

static class CmdCat
{
    public static void Run(string name)
    {
        string? mfPath = ModelfileParser.FindModelfile(name);

        if (mfPath is null)
        {
            Console.Error.WriteLine("No modelfile found for '" + name + "' in " + Paths.ModelfilesDir);
            Environment.Exit(1);
        }

        Console.Write(File.ReadAllText(mfPath));
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test tests/Veron.Tests.csproj --filter "FullyQualifiedName~CatCommandTests" -v normal`

Expected: All 3 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add Veron/Commands/CmdCat.cs
git commit -m "feat: add CmdCat to display raw modelfile content"
```

---

### Task 3: Wire Up in Program.cs

**Files:**
- Modify: `Veron/Program.cs`

- [ ] **Step 1: Add `cat` to the switch dispatch (around line 38-54)**

Change:
```csharp
        switch (command)
        {
            case "ls":
            case "list":    CmdList.Run(modelsDir); break;
```

To:
```csharp
        switch (command)
        {
            case "cat":     CmdCat.Run(opts.GetValueOrDefault("model")
                    ?? throw new ArgumentNullException("model argument required for cat")); break;
            case "ls":
            case "list":    CmdList.Run(modelsDir); break;
```

- [ ] **Step 2: Add `cat` to the help text (around line 68)**

Change:
```csharp
  ls, list            List all available modelfiles
```

To:
```csharp
  cat <name>          Show raw modelfile content
  ls, list            List all available modelfiles
```

- [ ] **Step 3: Build and verify no compile errors**

Run: `dotnet build Veron/Veron.csproj -c Release`

Expected: Build succeeds with zero errors.

- [ ] **Step 4: Smoke-test the command**

Run: `dotnet run --project Veron/Veron.csproj -- cat nonexistent-model` (should print error)

Run: `veron h` — verify `cat` appears in help output if `veron` is in PATH.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test tests/Veron.Tests.csproj -v normal`

Expected: All tests PASS including new CatCommandTests.

- [ ] **Step 6: Commit**

```bash
git add Veron/Program.cs
git commit -m "feat: wire up 'veron cat' command in dispatch and help"
```

---

### Task 4: Update README.md

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add `cat` to the commands table (around line 76)**

Change:
```markdown
| Command | Short | Description |
|---------|-------|-------------|
| `list` | `ls` | List all available modelfile profiles |
```

To:
```markdown
| Command | Short | Description |
|---------|-------|-------------|
| `cat <name>` | — | Show raw modelfile content |
| `list` | `ls` | List all available modelfile profiles |
```

- [ ] **Step 2: Add `veron cat qwopus` to the Quick Start examples (around line 43)**

After `# List all available model profiles` add a new example line:
```bash
# Show raw modelfile content
veron cat my-model
```

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: add veron cat to README commands table and quick start"
```

---

## Self-Review

**Spec coverage:**
- ✅ Raw file content dump — CmdCat.Run prints File.ReadAllText
- ✅ Uses FindModelfile for name resolution — same as serve/claude/run
- ✅ Error + exit 1 if not found — Environment.Exit(1) with stderr message
- ✅ No extra flags — single positional arg only
- ✅ Tests: happy path, not found, stem resolution
- ✅ Help text updated
- ✅ README updated

**Placeholder scan:** None. All code is complete, all test assertions are specific, all commands are exact.

**Type consistency:** `CmdCat.Run(string name)` matches switch dispatch via `opts.GetValueOrDefault("model")` — same pattern as CmdServe and CmdRun. Error message format matches ModelfileParser's own "No modelfile found" wording.
