# Design: Refactor Program.cs Into Layered Structure

## Purpose

Split the monolithic `Program.cs` (1053 lines) into fine-grained files organized by responsibility. Move `Veron.csproj` from repo root into its own `src/` folder for cleaner project structure. This is a pure refactor — no behavioral changes.

## Motivation

- `Program.cs` is 1053 lines containing commands, parsing, validation, process management, and data structures in one file. Hard to navigate, hard to reason about individual concerns.
- `Veron.csproj` sits at the repo root alongside README, docs, tests — it's not clear where project source starts.

## Architecture

### Directory Structure

```
Veron-AI-Serve-Cli/
├── src/
│   ├── Veron.csproj
│   └── Veron/
│       ├── Commands/
│       │   ├── CmdList.cs
│       │   ├── CmdCreate.cs
│       │   ├── CmdServe.cs
│       │   ├── CmdClaude.cs
│       │   └── CmdStop.cs
│       ├── Parsing/
│       │   ├── ModelfileParser.cs
│       │   └── CliParser.cs
│       ├── Validation/
│       │   ├── ModelfileValidator.cs
│       │   └── ClaudeCodeValidator.cs
│       ├── Process/
│       │   ├── LlamaServer.cs
│       │   └── PidManager.cs
│       ├── Models/
│       │   ├── ModelConfig.cs
│       │   └── ToolConfig.cs
│       ├── Testing/
│       │   └── ProgramTestHelper.cs
│       └── Program.cs
├── tests/
├── docs/
├── bin/
└── README.md
```

### File Responsibilities

| File | Contents | Current Lines |
|------|----------|---------------|
| `Models/ModelConfig.cs` | `ModelConfig` class — plain data, no logic | 795-807 |
| `Models/ToolConfig.cs` | `ToolConfig` class — plain data, no logic | 1034-1038 |
| `Parsing/CliParser.cs` | `ParseOpts`, `OptsBool`, `ExpandEnv`, `EscapeArg` | 885-933 |
| `Parsing/ModelfileParser.cs` | `LoadConfig`, `FindModelfile`, `ParseModelfile`, `ApplyParameter`, `ParseToolBlocks` | 307-536, 538-552 |
| `Validation/ModelfileValidator.cs` | `ValidateModelfile`, `IsValidName`, `KnownParams`, `ParameterExpectedType`, `ValidateParameterValue` | 554-561, 622-658, 709-758 |
| `Validation/ClaudeCodeValidator.cs` | `ValidateClaudeCodeParameter`, `ValidateToolBlocks`, known param lookup sets | 563-620, 759-791 |
| `Process/LlamaServer.cs` | `BuildLlamaCmd`, `ServerPsi`, `WaitForServer` | 811-856 |
| `Process/PidManager.cs` | `WritePid`, `ReadPid`, `DeletePid`, `IsProcessAlive` | 858-881 |
| `Commands/CmdList.cs` | `CmdList` | 49-100 |
| `Commands/CmdCreate.cs` | `CmdCreate` | 263-300 |
| `Commands/CmdServe.cs` | `CmdServe` | 102-134 |
| `Commands/CmdClaude.cs` | `CmdClaude` | 136-233 |
| `Commands/CmdStop.cs` | `CmdStop` | 235-261 |
| `Testing/ProgramTestHelper.cs` | Delegates to static methods for test project access | 1042-1052 |
| `Program.cs` | Constants, `Main()`, routing, `PrintUsage`, `PrintVersion` — ~80 lines | all remaining |

### Data Flow / Dependencies

One-way flow with no circular dependencies:

```
Program.cs (entry + routing)
    │
    ├──→ Commands/*  (command handlers)
    │         │
    │         ├──→ Parsing/*      (read files, parse args, build ModelConfig)
    │         │         │
    │         │         └──→ Models/*   (plain data classes)
    │         │
    │         ├──→ Validation/*  (validate parsed content)
    │         │         │
    │         │         ├──→ Parsing/*  (to parse TOOL blocks for validation)
    │         │         └──→ Models/*
    │         │
    │         └──→ Process/*     (start servers, manage PIDs)
    │                 │
    │                 └──→ Models/*
    │
    └──→ Testing/ProgramTestHelper  (delegates to wherever methods live now)
```

### Config Loading

The `LoadConfig()` method — shared by both `CmdServe` and `CmdClaude` — lives in `Parsing/ModelfileParser.cs` alongside `FindModelfile()`. Its job: find the modelfile, parse it, overlay CLI flags into a `ModelConfig`.

### Test Helper

`ProgramTestHelper` moves to `Testing/ProgramTestHelper.cs`. It delegates to whatever static methods live wherever — if `ParseToolBlocks` is in `ModelfileParser`, the helper calls `ModelfileParser.ParseToolBlocks(path)`. This avoids the test project needing a separate reference.

### csproj Changes

- Move `Veron.csproj` from root to `src/Veron.csproj`.
- Update `<Compile Include="**/*.cs" />` or rely on SDK-style auto-includes (default in .NET SDK projects — all `.cs` under the project dir are included automatically).
- The test project at `tests/` currently compiles Program.cs via `<Compile Include="../Program.cs">`. This reference needs updating to point to the new file locations, or switch to a proper project reference with `<ProjectReference>`.

### Backward Compatibility

- Same CLI interface, same commands, same options, same error messages, same exit codes.
- Build command may change: `dotnet publish -c Release -o bin/release` becomes `dotnet publish src/Veron.csproj -c Release -o bin/release` (or use a solution file).
- Binary output is the same.

### Testing

No behavioral changes — existing tests in the `tests/` directory should pass with minimal adjustment to file references.
