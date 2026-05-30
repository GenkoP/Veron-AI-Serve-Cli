# .NET 8 LTS Downgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Downgrade the Veron project from .NET 10 to .NET 8 LTS so users with older SDK versions can build and run it, while remaining fully compatible with the latest .NET SDK.

**Architecture:** No architectural change. Update `TargetFramework` in both `.csproj` files, downgrade test framework NuGet packages to versions supporting .NET 8, update the README, and verify the project builds and tests pass. Zero source code changes needed — the codebase uses no .NET 10-specific features.

**Tech Stack:** .NET 8 SDK, Microsoft.NET.Test.Sdk 17.8.0, xunit 2.6.6

---

### Task 1: Update Veron main project to net8.0

**Files:**
- Modify: `Veron/Veron.csproj:9`

- [ ] **Step 1: Change TargetFramework**

In `Veron/Veron.csproj`, change line 9 from `<TargetFramework>net10.0</TargetFramework>` to `<TargetFramework>net8.0</TargetFramework>`.

```xml
<TargetFramework>net8.0</TargetFramework>
```

- [ ] **Step 2: Commit**

```bash
git add Veron/Veron.csproj
git commit -m "chore: downgrade main project to net8.0"
```

---

### Task 2: Update test project and package versions

**Files:**
- Modify: `tests/Veron.Tests.csproj`

- [ ] **Step 1: Change TargetFramework and package versions**

In `tests/Veron.Tests.csproj`:

Change `<TargetFramework>net10.0</TargetFramework>` to `<TargetFramework>net8.0</TargetFramework>`.

Change the three package references:
- `Microsoft.NET.Test.Sdk` version `17.12.0` → `17.8.0`
- `xunit` version `2.9.3` → `2.6.6`
- `xunit.runner.visualstudio` version `2.8.2` → `2.4.5`

The file should look like:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Veron\Veron.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.4.5" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Commit**

```bash
git add tests/Veron.Tests.csproj
git commit -m "chore: downgrade test project to net8.0 and compatible package versions"
```

---

### Task 3: Update README requirements section

**Files:**
- Modify: `README.md:6-7`

- [ ] **Step 1: Change .NET version requirement**

In `README.md`, change line 7 from:
```
- **.NET 10 SDK** (tested with 10.0.108) — [install from dot.net](https://dotnet.microsoft.com/download/dotnet/10.0)
```
to:
```
- **.NET 8+ SDK** — [install from dot.net](https://dotnet.microsoft.com/download/dotnet/8.0)
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: update requirements to .NET 8+ SDK"
```

---

### Task 4: Clean build artifacts and verify restore

**Files:**
- Delete: `Veron/bin/`, `Veron/obj/`, `tests/bin/`, `tests/obj/`

- [ ] **Step 1: Remove stale build output**

```bash
rm -rf Veron/bin Veron/obj tests/bin tests/obj
```

- [ ] **Step 2: Restore packages for the new target framework**

```bash
dotnet restore
```

Expected: restores succeed with no errors, pulling .NET 8 TFMs.

- [ ] **Step 3: Commit** (no file changes to commit — this step ensures clean state before build)

---

### Task 5: Verify the project builds and publishes

- [ ] **Step 1: Publish the release binary**

```bash
dotnet publish Veron/Veron.csproj -c Release -o bin/release
```

Expected: build succeeds, binary appears at `bin/release/Veron`.

- [ ] **Step 2: Verify the published binary runs**

```bash
./bin/release/Veron --help
```

Expected: prints the usage text and exits with code 0.

---

### Task 6: Run all tests to verify they still pass

- [ ] **Step 1: Run the full test suite**

```bash
dotnet test
```

Expected: all existing tests pass. If any test fails due to xunit API differences between 2.6.6 and 2.9.3, note the failure and fix in Task 7.

- [ ] **Step 2: Commit if there were fixes, otherwise just verify**

If no failures, proceed. If there were fixes:

```bash
git add .
git commit -m "fix: adapt tests for xunit 2.6.6 API"
```

---

### Task 7: Fix any test failures from xunit version downgrade (if needed)

**Contingency task — only execute if Task 6 revealed failures.**

Common issues when downgrading xunit from 2.9.x to 2.6.x:
- `Assert.Matches` was removed; use `Assert.True(value.Regex.IsMatch(value))` instead
- Attribute constructor signatures may differ slightly

**Files:** whatever test files fail in Task 6

- [ ] **Step 1: Read the failing test file and identify the incompatible API call**

- [ ] **Step 2: Replace with the xunit 2.6.x equivalent**

- [ ] **Step 3: Re-run `dotnet test` to confirm it passes**

- [ ] **Step 4: Commit**

```bash
git add <changed-test-files>
git commit -m "fix: adapt test assertions for xunit 2.6.6"
```

---

## Self-Review Checklist

| Check | Result |
|-------|--------|
| Spec coverage: Task 1 covers Veron.csproj, Task 2 covers tests csproj + packages, Task 3 covers README, Task 4-6 cover verification | All spec items have tasks |
| Placeholder scan: no "TBD", "TODO", or vague steps | Clean |
| Type consistency: N/A — no new code defined | N/A |
| Scope: focused on single change (downgrade to net8.0) | Focused |
