# Downgrade Veron to .NET 8 LTS

## Goal

Allow users with older .NET SDK versions to build and run the Veron project while still being fully compatible with the latest .NET SDK versions.

## Background

The project currently targets `net10.0`. A full audit of all source files confirmed that **zero .NET 10-specific features are used** — no raw string literals, collection expressions, list patterns, or inline arrays. The code's language feature floor is C# 8 / .NET Core 3.0 (nullable reference types, ranges, switch expressions, pattern matching).

The real blocker was the test SDK package versions (`Microsoft.NET.Test.Sdk` 17.12.0 requires .NET SDK 9+), not the source code.

## Approach: Target net8.0

Change both project files to target `net8.0` and downgrade test framework packages to versions compatible with .NET 8.

**No source code changes needed.**

### Compatibility

- .NET 8 is LTS (supported until Nov 2026, extended support to 2028)
- Any .NET SDK 9+, 10+ can build projects targeting `net8.0` — the SDK is forward-compatible
- Users with .NET 8, 9, or 10 SDK can all build this project

## Changes

### Project Files

| File | Change |
|------|--------|
| `Veron/Veron.csproj` | `TargetFramework`: `net10.0` → `net8.0` |
| `tests/Veron.Tests.csproj` | `TargetFramework`: `net10.0` → `net8.0` |

### Test Package Versions

| Package | Before | After |
|---------|--------|-------|
| Microsoft.NET.Test.Sdk | 17.12.0 | 17.8.0 |
| xunit | 2.9.3 | 2.6.6 |
| xunit.runner.visualstudio | 2.8.2 | 2.4.5 |

### Documentation

- **README.md**: update "Requirements" from ".NET 10 SDK" → ".NET 8+ SDK"

### Build Artifacts

- Clean `bin/` and `obj/` directories so they rebuild for net8.0

## Package Version Rationale

- **Microsoft.NET.Test.Sdk 17.8.0** — last version officially supporting .NET 8; version 17.12.0 requires SDK 9+
- **xunit 2.6.6** — stable, known-good pairing with SDK 17.8 and .NET 8
- **xunit.runner.visualstudio 2.4.5** — matches xunit 2.6.x line

## Risk Assessment

**Low risk.** Confirmed by auditing all source files — no .NET 9 or .NET 10 language features or APIs are used. The only risk surface is the xunit version downgrade, but 2.6.6 is mature and test API surface between 2.6.6 and 2.9.3 is stable.

## Verification

After changes:
1. `dotnet publish Veron/Veron.csproj -c Release -o bin/release` succeeds
2. `dotnet test` passes all existing tests
3. Published binary runs correctly
