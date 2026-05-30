# Directory.Build.props Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Share common MSBuild properties across project files via `Directory.Build.props` to eliminate duplication.

**Architecture:** Create a single `Directory.Build.props` at the repo root inheriting target framework, implicit usings, and nullable settings. Each `.csproj` retains only its own unique properties.

**Tech Stack:** .NET SDK, MSBuild

---

### Task 1: Create Directory.Build.props

**Files:**
- Create: `Directory.Build.props`

- [ ] **Step 1: Write the file**

```xml
<Project>
  <PropertyGroup>
    <DefaultFramework>net10.0</DefaultFramework>

    <TargetFrameworks Condition="'$(AllFrameworks)' == 'true'">net8.0;net10.0</TargetFrameworks>
    <TargetFramework Condition="'$(AllFrameworks)' != 'true'">$(DefaultFramework)</TargetFramework>

    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Commit**

```bash
git add Directory.Build.props
git commit -m "feat: add Directory.Build.props for shared project settings"
```

### Task 2: Update Veron.csproj

**Files:**
- Modify: `Veron/Veron.csproj`

- [ ] **Step 1: Replace the file content**

Remove the shared properties (`DefaultFramework`, `TargetFrameworks`/`TargetFramework`, `ImplicitUsings`, `Nullable`). Keep only project-specific settings:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="Veron.Tests" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>Veron</RootNamespace>
    <SingleFile>true</SingleFile>
    <SelfContained>false</SelfContained>
    <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
  </PropertyGroup>

 </Project>
```

- [ ] **Step 2: Verify the build**

Run: `dotnet build Veron/Veron.csproj`
Expected: Build succeeds with no errors or warnings.

Run: `dotnet build Veron/Veron.csproj -p:AllFrameworks=true`
Expected: Builds for both net8.0 and net10.0.

- [ ] **Step 3: Commit**

```bash
git add Veron/Veron.csproj
git commit -m "refactor: remove shared properties from Veron.csproj (inherited from Directory.Build.props)"
```

### Task 3: Update Veron.Tests.csproj

**Files:**
- Modify: `tests/Veron.Tests.csproj`

- [ ] **Step 1: Replace the file content**

Remove `TargetFramework`, `ImplicitUsings`, and `Nullable` — they come from `Directory.Build.props`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
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

- [ ] **Step 2: Verify the build and tests**

Run: `dotnet build tests/Veron.Tests.csproj`
Expected: Build succeeds with no errors or warnings.

Run: `dotnet test tests/Veron.Tests.csproj`
Expected: All tests pass.

Run: `dotnet build tests/Veron.Tests.csproj -p:AllFrameworks=true`
Expected: Builds for both net8.0 and net10.0.

- [ ] **Step 3: Commit**

```bash
git add tests/Veron.Tests.csproj
git commit -m "refactor: remove shared properties from Veron.Tests.csproj (inherited from Directory.Build.props)"
```
