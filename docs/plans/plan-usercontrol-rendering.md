# Plan: UserControl and Custom Control Rendering

## Status: Mostly Complete

The original plan proposed changes across three renderers (SyntaxTreeFormRenderer, InProcessFormRenderer, CompiledFormRenderer). All three have been replaced by **DesignSurfaceFormRenderer**, which uses .NET's `DesignSurface`/`IDesignerHost` infrastructure — the same system Visual Studio uses.

## What's Done

- **Top-level UserControl rendering**: DesignSurfaceFormRenderer detects Form vs UserControl via `DetectBaseType()` (companion file regex + heuristics) and creates the appropriate DesignSurface container.
- **Custom control loading**: `ResolveProjectAssemblyPaths()` loads DLLs from the project's `bin/` directory. DesignSurface handles instantiation automatically.
- **Out-of-process rendering**: RendererProcessPool dispatches to framework-matched host processes (net48, netcoreapp3.1, net8.0-windows), so custom controls built for any framework resolve correctly.
- **Test coverage**: `RenderDesignerCode_UserControl_DetectsAndRenders` and `RenderDesignerCode_StatusDashboard_RendersAsUserControl` confirm UserControl path works.

## What Remains

### 1. Error-resilient control rendering

When a control throws during initialization (missing dependency, broken constructor, etc.), the entire render currently fails. Visual Studio instead shows a red error box for the broken control and continues rendering the rest of the form.

**Approach**: Wrap individual control creation in try/catch within the DesignSurface statement executor. On failure, substitute a red-bordered Panel with the exception message — matching VS behavior.

### 2. Recursive UserControl rendering from source

When a form references a UserControl defined in the same project (e.g., `this.statusDash = new MyApp.StatusDashboard()`), DesignSurface can only render it if the project has been built (so the DLL exists in `bin/`). If the project hasn't been built, the control is skipped.

**Approach**: When a type can't be resolved from loaded assemblies, search the project directory for a matching `.Designer.cs` file and parse it recursively via a child DesignSurface. Needs circular reference protection.

### 3. Placeholder rendering for unresolvable types

When a control type can't be resolved at all (no DLL, no designer file), render a gray placeholder with the type name instead of leaving a blank gap.

## Obsolete Sections

The original plan's sections on SyntaxTreeFormRenderer refactoring (`_form` → `_container`), InProcessFormRenderer `GenerateCodeBehind` changes, CompiledFormRenderer `GenerateProgram` changes, and the SyntaxFactory vs StringBuilder analysis are all obsolete — those renderers have been deleted.
