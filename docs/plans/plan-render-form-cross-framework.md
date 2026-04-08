# Plan: Cross-Framework Compatibility for `render_form`

## Context

The `render_form` tool was just implemented (all files already exist, build passes, 42 tests pass). The user wants to ensure it works for designer files from **all .NET ecosystems**: .NET Framework 4.x, .NET Core 3.x, and modern .NET 5-8+.

**Key insight: No multi-targeting/multiple builds needed.** The `render_form` tool is inherently framework-agnostic because:
1. **Roslyn 4.12 parses all C# versions** — designer files are just text, parsed without compilation
2. **Standard WinForms controls are the same** — `Button`, `TextBox`, `Label`, etc. have identical constructors and properties across all frameworks
3. **The tool runs on .NET 8** and uses its own runtime's types — it never loads the target project's assemblies

The only real gap: **.NET Framework legacy controls that were removed in .NET Core**. These types don't exist in the .NET 8 runtime, so `ResolveType` returns null and they're silently skipped. The user gets a partial render with no indication of what was omitted.

## What Needs to Change

### 1. MODIFY: `FormRenderer.cs` — Add `skippedTypes` tracking and return render result

Currently failures are silently swallowed. We need to:
- Track skipped types (unresolvable type names) in a `List<string>`
- Track skipped statements (any catch block that fires) with a count
- Return a `RenderResult` object with: `bool Success`, `string OutputPath`, `List<string> SkippedTypes`, `int SkippedStatements`
- The `RenderDesignerCode`/`RenderDesignerFile` methods return `RenderResult` instead of `void`

This gives callers (and the MCP response) visibility into what was omitted.

### 2. MODIFY: `Program.cs` — Include skipped types in MCP response

The `RenderForm` handler currently returns just `{"success": true, "message": "..."}`. Change to include:
```json
{
  "success": true,
  "message": "Form rendered to ...",
  "skippedTypes": ["System.Windows.Forms.MainMenu", "System.Windows.Forms.StatusBar"],
  "skippedStatements": 3
}
```

This tells Claude Code exactly which controls were omitted so it can inform the user.

### 3. MODIFY: `FormRendererTests.cs` — Test the new return type

- Update existing tests that call `RenderDesignerCode` to check the returned `RenderResult`
- Add test: designer with legacy type name → verify it appears in `SkippedTypes`
- Add test: designer with all-resolvable types → verify `SkippedTypes` is empty

### 4. MODIFY: Documentation — Note cross-framework support and limitations

In README.md "Form Preview" section and docs/CLAUDE_CODE_SETUP.md, add:
- Works with designer files from .NET Framework 4.x, .NET Core, and .NET 5+
- Legacy controls removed in .NET Core (`DataGrid`, `MainMenu`, `MenuItem`, `ContextMenu`, `ToolBar`, `StatusBar`) will be reported as skipped but won't prevent rendering
- Default font may differ slightly (.NET Framework uses MS Sans Serif 8.25pt; .NET 8 uses Segoe UI 9pt) — cosmetic only

## Files to Modify

| File | Change |
|------|--------|
| `src/Rhombus.WinFormsMcp.Server/Automation/FormRenderer.cs` | Add `RenderResult` class, track skipped types/statements, change return types |
| `src/Rhombus.WinFormsMcp.Server/Program.cs` | Update `RenderForm` handler to include skippedTypes in response |
| `tests/Rhombus.WinFormsMcp.Tests/FormRendererTests.cs` | Update assertions for `RenderResult`, add skipped-type tests |
| `README.md` | Add cross-framework note to Form Preview section |
| `docs/CLAUDE_CODE_SETUP.md` | Add cross-framework note |

## Verification

1. `dotnet build Rhombus.WinFormsMcp.sln`
2. `dotnet test Rhombus.WinFormsMcp.sln` — all existing + new tests pass
3. Verify: designer file with `System.Windows.Forms.MainMenu` → renders without crash, reports MainMenu in skippedTypes
4. Verify: standard designer file → SkippedTypes is empty
