---
name: cat-command-design
description: "Design for `veron cat <name>` — display raw modelfile content"
metadata:
  type: project
---

# Cat Command Design

## Overview

Add a `cat` command to Veron that displays the raw contents of a modelfile profile. Equivalent to `cat ~/.veron/modelfiles/<name>` but with the same name resolution as other commands (exact match, stem match, extension-agnostic).

## Command

```
veron cat <name>
```

### Arguments

- `<name>` — modelfile profile name (required). Same resolution rules as `serve`, `claude`, `run`.

### Behavior

1. Resolve the modelfile path using `ModelfileParser.FindModelfile(name)`
2. If found → print file content to stdout, exit 0
3. If not found → print error to stderr, exit 1

### No extra flags

No options beyond `<name>`. This is a dump-and-exit command.

## Implementation

**New file:** `Veron/Commands/CmdCat.cs`

```csharp
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

**Existing files touched:**

- `Veron/Program.cs` — wire up `cat` command in the switch dispatch and add to help text
- `tests/CatCommandTests.cs` — test file using `ProgramTestHelper` pattern

## Testing

Using the same pattern as `CatCommandTests.cs` (see other test files for reference):

1. **Happy path** — create a modelfile, run `cat`, verify output matches content
2. **Not found** — run `cat` with nonexistent name, verify error + exit code 1
3. **Name resolution** — verify stem matching works (e.g. modelfile named `my-model.modelfile`, run `cat my-model`)

## Scope

Focused on one thing: show raw modelfile content. No formatting, no enrichment, no additional flags.
