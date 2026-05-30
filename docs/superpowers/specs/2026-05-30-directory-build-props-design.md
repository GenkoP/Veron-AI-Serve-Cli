---
name: directory-build-props
description: Share common MSBuild properties via Directory.Build.props
---

# Design: Shared Project Settings via Directory.Build.props

## Purpose

Eliminate duplicated MSBuild properties across project files by introducing a `Directory.Build.props` at the repo root. Adding new projects or changing shared settings (e.g., default framework) is then a one-place change.

## Changes

### New file: `/Directory.Build.props`

Holds shared properties inherited by all `.csproj` files in the repo:

- `DefaultFramework` — `net10.0`
- Conditional `TargetFrameworks` / `TargetFramework` — honors `AllFrameworks=true` for multi-framework builds, defaults to `$(DefaultFramework)`
- `ImplicitUsings` — `enable`
- `Nullable` — `enable`

### Updated: `/Veron/Veron.csproj`

Removes shared properties (target framework, implicit usings, nullable). Keeps only project-specific settings:

- `OutputType`, `RootNamespace`, `SingleFile`, `SelfContained`, `RuntimeIdentifier`
- `InternalsVisibleTo` for test project

### Updated: `/tests/Veron.Tests.csproj`

Removes shared properties (target framework, implicit usings, nullable). Keeps only test-specific settings:

- `IsTestProject`
- Package references
- Project reference

## Behavior

No behavioral change — the same properties are set, just from a different location. Builds produce identical outputs.
