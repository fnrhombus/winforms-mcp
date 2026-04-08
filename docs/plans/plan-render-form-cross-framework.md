# Plan: Cross-Framework Compatibility for Form Renderers

## Problem Statement

The MCP server runs on .NET 8. Users may point it at WinForms projects targeting .NET Framework 4.x, .NET Core 3.1, .NET 5, .NET 6, .NET 7, or .NET 8. The original plan dismissed cross-framework concerns because the only renderer at the time was a text parser. Now two of the three renderers actually compile and/or load assemblies, which introduces real cross-framework failure modes.

## Renderer-by-Renderer Analysis

### 1. SyntaxTreeFormRenderer

**How it works:** Parses `InitializeComponent()` via Roslyn syntax tree, then creates .NET 8 WinForms controls via `Activator.CreateInstance` and reflection against the currently loaded (i.e., .NET 8) runtime assemblies.

**Cross-framework issues:**

| Issue | Severity | Details |
|-------|----------|---------|
| Missing types | Medium | .NET Framework types removed in .NET Core: `MainMenu`, `MenuItem`, `ContextMenu`, `ToolBar`, `StatusBar`, `DataGrid`. `ResolveType` returns null, control is silently skipped. |
| Property/method signature differences | Low | Some properties exist in both frameworks but with different types or behavior. Example: `Font` default changed from "Microsoft Sans Serif 8.25pt" (.NET Framework) to "Segoe UI 9pt" (.NET 8). These don't crash but produce slightly different layouts. |
| Designer syntax differences | Low | .NET Framework designer generates `new System.EventHandler(this.button1_Click)` while modern designers generate `this.button1_Click += this.button1_Click`. Both are handled: the renderer skips `+=` assignments entirely. |
| No assembly loading | N/A | This renderer never loads external assemblies. Custom/third-party controls are silently skipped because their types aren't in the .NET 8 runtime. This is framework-independent -- it fails equally for .NET 8 custom controls. |

**Summary:** Works for any TFM's designer file as long as the controls used exist in .NET 8 WinForms. Degrades gracefully (silent skip) when they don't. The failure mode is the same regardless of source TFM -- it's purely about whether the type exists in the .NET 8 runtime.

---

### 2. InProcessFormRenderer

**How it works:** Compiles the designer code in-memory with Roslyn using the MCP server's own loaded assemblies as references (all .NET 8). Then loads the compiled assembly into the current .NET 8 process via `Assembly.Load`. Also calls `ResolveProjectAssemblies` to find DLLs in the source project's `bin/` folder, adds them as Roslyn `MetadataReference`s, and loads them into the runtime via `Assembly.LoadFrom`.

**Cross-framework issues:**

| Issue | Severity | Details |
|-------|----------|---------|
| Roslyn compiles against .NET 8 assemblies | **High** | `GetMetadataReferences()` uses `AppDomain.CurrentDomain.GetAssemblies()` -- these are all .NET 8 assemblies. If the designer code uses .NET Framework-only APIs (e.g., `MainMenu`, `DataGrid`), Roslyn compilation fails with CS0246 "type not found". This is actually better than silent skipping -- the error message tells you exactly what's wrong. |
| Loading .NET Framework DLLs into .NET 8 process | **High** | `ResolveProjectAssemblies` scans `bin/Debug/<tfm>/` and calls `Assembly.LoadFrom`. If the source project targets `net48`, those DLLs are .NET Framework assemblies. Loading .NET Framework assemblies into a .NET 8 process is not supported and will throw `BadImageFormatException` or silently fail. Even if loading succeeds, type identity won't match: a `Button` from a net48 assembly is not the same `Button` the .NET 8 Roslyn compilation expects. |
| TFM directory selection is naive | Medium | `ResolveProjectAssemblies` picks the most recently written TFM directory. For a multi-targeting project (`net48;net8.0-windows`), it might pick `net48` DLLs. For a single-target `net48` project, it will always pick net48 DLLs. |
| NuGet package compatibility | Medium | Third-party WinForms control packages may only target `net48` or only target `net6.0+`. The in-memory compilation uses .NET 8 references, so if the package's API surface differs between frameworks, compilation may fail or behave differently. |
| No binding redirects | Low | .NET Framework projects rely on binding redirects in app.config. `Assembly.LoadFrom` in .NET 8 ignores these entirely. Version mismatches in transitive dependencies will cause `FileLoadException`. |

**Summary:** Fundamentally broken for .NET Framework projects. The renderer compiles against .NET 8 and loads assemblies into a .NET 8 process -- there is no path to making this work with net48 assemblies without significant architectural changes. Works well for .NET 6/7/8 projects where the assemblies are compatible.

---

### 3. CompiledFormRenderer

**How it works:** Generates a temporary `.csproj`, copies the `TargetFramework` from the source project's csproj, copies `PackageReference` elements, adds a `<Reference>` to the source project's built DLL, then runs `dotnet run` as a child process. The child process is a completely separate build and execution.

**Cross-framework issues:**

| Issue | Severity | Details |
|-------|----------|---------|
| TFM passthrough to temp project | **High** | `GenerateCsproj` copies the source project's `TargetFramework` verbatim. If the source targets `net48`, the temp project targets `net48`. But `dotnet run` for net48 requires the .NET Framework Developer Pack to be installed, and the `Sdk="Microsoft.NET.Sdk"` style project needs MSBuild to resolve Framework references. This **may actually work** if the developer has the .NET Framework targeting pack installed -- `dotnet build` can build net48 SDK-style projects. |
| .NET Framework targeting pack not installed | **High** | Many machines with .NET 8 SDK don't have the .NET Framework 4.8 targeting pack. The temp project build will fail with "The reference assemblies for .NETFramework,Version=v4.8 were not found." |
| Assembly reference to source DLL | Medium | `FindProjectOutputDll` picks the most recently written TFM directory's DLL. If the source project targets net48, this is a .NET Framework DLL being referenced by a temp project that also targets net48 -- this should work fine since both are the same TFM. |
| UseWindowsForms hardcoded | Low | The temp project always sets `<UseWindowsForms>true</UseWindowsForms>`. For net48, this property is ignored (WinForms references come from the framework itself). For .NET Core 3.1+, this is correct. No actual issue. |
| Designer syntax: event handler delegates | Low | `ParseDesignerFile` regex handles both `+= new EventHandler(this.Handler)` and `+= this.Handler`. Works across frameworks. |
| Multi-targeting projects | Medium | `GenerateCsproj` reads `TargetFramework` (singular). If the source project uses `TargetFrameworks` (plural, e.g., `net48;net8.0-windows`), this element won't be found and defaults to `net8.0-windows`. The generated code-behind uses nullable syntax (`object? sender`) which won't compile under older C# language versions unless `<LangVersion>` is set. |
| Package version conflicts | Low | Packages are copied verbatim from the source project. If a package doesn't support the TFM or has been yanked, the restore will fail. This is a general build issue, not cross-framework specific. |

**Summary:** The best positioned renderer for cross-framework support because it runs a real `dotnet build` in the correct TFM. The main barrier is whether the machine has the right SDK/targeting pack installed. For .NET 6/7/8 projects, it should just work. For .NET Framework projects, it depends on the machine's installed SDKs.

---

## Compatibility Matrix: What Works Today

| Source Project TFM | SyntaxTree | InProcess | Compiled |
|---------------------|-----------|-----------|----------|
| net8.0-windows | Works (standard controls only) | Works (including custom controls) | Works |
| net7.0-windows | Works (standard controls only) | Mostly works (minor API diffs unlikely) | Works if .NET 7 SDK installed |
| net6.0-windows | Works (standard controls only) | Mostly works | Works if .NET 6 SDK installed |
| net5.0-windows | Works (standard controls only) | Probably works | Works if .NET 5 SDK installed |
| netcoreapp3.1 | Works (standard controls only) | Risky (assembly compat) | Works if .NET Core 3.1 SDK installed |
| net48 | Works minus removed types | **Broken** (assembly loading fails) | Works if .NET Framework 4.8 targeting pack installed |
| net472 / net461 | Works minus removed types | **Broken** | Works if targeting pack installed |

## Proposed Fixes and Mitigations

### Phase 1: Immediate (low effort, high impact)

#### 1a. InProcessFormRenderer: Detect TFM mismatch and fail fast

Instead of silently loading incompatible assemblies, detect the source project's TFM and refuse to load assemblies from incompatible frameworks.

**Changes to `InProcessFormRenderer.ResolveProjectAssemblies`:**
- Parse the source project's csproj to determine TFM
- If TFM starts with `net4` (i.e., .NET Framework), skip assembly loading entirely and let compilation proceed with .NET 8 references only. This means custom controls from the project won't render, but standard controls will compile and render correctly.
- If TFM is a compatible .NET Core/.NET 5+ version, proceed as today
- Add a log/warning message indicating why custom controls were skipped

**Files:** `InProcessFormRenderer.cs`

#### 1b. InProcessFormRenderer: Prefer compatible TFM directories

When scanning `bin/Debug/` for DLLs, prefer TFM directories that match the running runtime. Currently it picks the most recently written directory, which may be `net48`.

**Changes to `InProcessFormRenderer.ResolveProjectAssemblies`:**
- Sort TFM directories by compatibility: `net8.0-windows` > `net7.0-windows` > `net6.0-windows` > skip `net4*`
- Fall back to most-recent if no compatible TFM directory found (but with the Phase 1a guard, net4x dirs would be skipped)

**Files:** `InProcessFormRenderer.cs`

#### 1c. CompiledFormRenderer: Handle multi-target projects

Read `TargetFrameworks` (plural) and pick the best one, preferring `net8.0-windows` over older versions.

**Changes to `CompiledFormRenderer.GenerateCsproj`:**
- Check for `TargetFrameworks` (plural) element in addition to `TargetFramework` (singular)
- If plural, split on `;` and pick the highest .NET version that supports WinForms
- Prefer `net8.0-windows` > `net7.0-windows` > `net6.0-windows` > `net48` as last resort

**Files:** `CompiledFormRenderer.cs`

#### 1d. CompiledFormRenderer: Fix nullable syntax for older TFMs

The generated code-behind uses `object? sender` which requires C# 8+. If targeting `net48` without a `<LangVersion>` override, this fails.

**Changes to `CompiledFormRenderer.GenerateCodeBehind`:**
- Use `object sender` instead of `object? sender` (safe -- it's a stub method body)
- Or: add `<LangVersion>latest</LangVersion>` to the generated csproj

**Files:** `CompiledFormRenderer.cs` (either `GenerateCodeBehind` or `GenerateCsproj`)

### Phase 2: Medium effort, good UX improvement

#### 2a. SyntaxTreeFormRenderer: Report skipped types

Add `SkippedTypes` tracking so the MCP response tells the caller exactly which controls were omitted. This lets Claude Code inform the user and suggest switching to `render_form_compiled` for full fidelity.

**Changes:**
- Add `List<string> SkippedTypes` field, populated when `ResolveType` returns null
- Add `RenderResult` return type with `OutputPath`, `SkippedTypes`, `SkippedStatements`
- Update `Program.cs` to include skipped info in MCP response

**Files:** `SyntaxTreeFormRenderer.cs`, `Program.cs`, `SyntaxTreeFormRendererTests.cs`

#### 2b. CompiledFormRenderer: Detect missing SDK/targeting pack before build

Before running `dotnet run`, check if the required SDK is available. For net48, check if the targeting pack exists. Provide a clear error message instead of a cryptic MSBuild failure.

**Changes to `CompiledFormRenderer`:**
- After determining TFM, run a quick check (e.g., verify the targeting pack path exists, or parse `dotnet --list-sdks` output)
- If missing, return a helpful error: "Source project targets net48 but .NET Framework 4.8 targeting pack is not installed. Install it from https://... or build the project first and use render_form_inprocess."

**Files:** `CompiledFormRenderer.cs`

### Phase 3: Hard / architectural (future consideration)

#### 3a. InProcessFormRenderer: Out-of-process compilation for .NET Framework

Instead of compiling in the .NET 8 process, shell out to a .NET Framework `csc.exe` (if available) or use `dotnet build` like `CompiledFormRenderer` but load the result back in-process. This is essentially what `CompiledFormRenderer` already does, so the value of this change is questionable -- it might be better to just recommend `CompiledFormRenderer` for net4x projects.

**Verdict:** Probably not worth doing. The three-renderer architecture already provides the right tool for each situation.

#### 3b. Cross-framework type mapping for SyntaxTreeFormRenderer

Map removed .NET Framework types to .NET 8 equivalents where possible:
- `MainMenu` -> `MenuStrip`
- `StatusBar` -> `StatusStrip`  
- `ToolBar` -> `ToolStrip`
- `DataGrid` -> `DataGridView`

The property APIs are completely different between these types, so this would require translation logic for each type's properties. Very high effort for marginal benefit.

**Verdict:** Not practical. Better to report the skip and suggest using `render_form_compiled` with the Framework targeting pack.

## What's Genuinely Hard vs. Easy

### Easy
- Detecting TFM mismatches and failing fast with good messages
- Preferring compatible TFM directories when loading assemblies
- Handling `TargetFrameworks` (plural) in the compiled renderer
- Fixing nullable syntax in generated code
- Tracking and reporting skipped types

### Hard but possible
- Supporting .NET Framework projects in `CompiledFormRenderer` (requires targeting pack installation, but the code path already works if the pack is there)
- Pre-flight SDK detection to give clear error messages

### Effectively impossible without fundamental redesign
- Loading .NET Framework assemblies into a .NET 8 process (`InProcessFormRenderer` with net48 DLLs)
- Making `SyntaxTreeFormRenderer` instantiate types that don't exist in .NET 8
- Automatically translating removed type APIs to their modern equivalents

## Recommended Approach

The three-renderer architecture already provides the right answer for each scenario:

1. **Same-framework projects** (net8.0-windows): All three renderers work. Use `render_form` for speed, `render_form_inprocess` for custom controls, `render_form_compiled` for full fidelity.

2. **Recent .NET projects** (net6.0/net7.0): All three renderers work with minor caveats. `InProcessFormRenderer` may have minor API surface differences but will likely succeed.

3. **.NET Framework projects** (net4x): `SyntaxTreeFormRenderer` works with graceful degradation (skip removed types). `CompiledFormRenderer` works if the targeting pack is installed. `InProcessFormRenderer` is not viable.

The implementation priority should be:
1. Phase 1a-1d: Prevent bad failures, pick the right TFM directories, fix generated code
2. Phase 2a: Skipped-type reporting so users know what's missing
3. Phase 2b: Clear error messages when SDKs are missing
4. Phase 3: Skip -- the architecture already handles this via renderer selection

## Files to Modify

| File | Phase | Change |
|------|-------|--------|
| `src/.../Automation/InProcessFormRenderer.cs` | 1a, 1b | TFM detection, compatible TFM dir selection, skip net4x assemblies |
| `src/.../Automation/CompiledFormRenderer.cs` | 1c, 1d, 2b | Multi-target support, fix nullable syntax, SDK pre-check |
| `src/.../Automation/SyntaxTreeFormRenderer.cs` | 2a | `RenderResult` return type, `SkippedTypes` tracking |
| `src/.../Program.cs` | 2a | Include skipped types in `render_form` MCP response |
| `tests/.../SyntaxTreeFormRendererTests.cs` | 2a | Tests for `RenderResult` and skipped-type tracking |
| `tests/.../CompiledFormRendererTests.cs` | 1c, 1d | Tests for multi-target parsing, nullable fix |

## Verification

1. `dotnet build Rhombus.WinFormsMcp.sln` -- builds clean
2. `dotnet test Rhombus.WinFormsMcp.sln` -- all tests pass
3. Manual: point `render_form` at a net48 project's designer file -- renders with skipped types reported
4. Manual: point `render_form_inprocess` at a net48 project -- fails fast with clear message instead of `BadImageFormatException`
5. Manual: point `render_form_compiled` at a net48 project on a machine with targeting pack -- succeeds
6. Manual: point `render_form_compiled` at a net48 project without targeting pack -- clear error message
