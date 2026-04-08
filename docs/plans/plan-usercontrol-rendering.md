# Plan: UserControl and Custom Control Rendering

## Current State Analysis

### Renderer Summary

| Renderer | Mechanism | Custom Controls | Speed |
|---|---|---|---|
| **SyntaxTreeFormRenderer** | Parses .Designer.cs via Roslyn syntax tree, creates controls via reflection | No -- `ResolveType` only finds types in loaded assemblies (System.Windows.Forms, System.Drawing, etc.) | ~150ms |
| **InProcessFormRenderer** | Compiles .Designer.cs + generated code-behind in-memory with Roslyn, loads assembly, runs real InitializeComponent | Yes -- loads DLLs from project's bin/ output | ~450ms |
| **CompiledFormRenderer** | Generates a temp .csproj, runs `dotnet build` + `dotnet run` externally, captures base64 PNG from stdout | Yes -- references project's built DLL and copies PackageReferences | ~2800ms |

### Problem 1: Top-Level UserControl Rendering

None of the three renderers can render a UserControl's .Designer.cs as a top-level element. All three hardcode `Form` as the base type:

- **SyntaxTreeFormRenderer** (line 58): `_form = new Form()` -- always creates a Form as the top-level container.
- **InProcessFormRenderer** (line 95): `GenerateCodeBehind` emits `partial class {className} : Form` -- always inherits from Form.
- **CompiledFormRenderer** (line 235): `GenerateCodeBehind` emits `partial class {className} : Form`, and `GenerateProgram` casts to `Form` and calls `form.Show()`.

If you feed `StatusDashboard.Designer.cs` to any renderer, you get a broken result because:
- The designer code sets `this.Size` (a UserControl property), not `this.ClientSize` (a Form property).
- SyntaxTreeFormRenderer creates a Form but the designer code applies UserControl-specific properties.
- InProcessFormRenderer/CompiledFormRenderer will fail to compile because the designer partial class declares `partial class StatusDashboard` but the generated code-behind says `partial class StatusDashboard : Form`, while the real code-behind says `: UserControl`.

### Problem 2: Recursive UserControl Rendering (SyntaxTreeFormRenderer)

When `KitchenSinkForm.Designer.cs` contains:
```csharp
this.statusDash = new KitchenSink.StatusDashboard();
```

`SyntaxTreeFormRenderer.ResolveType("KitchenSink.StatusDashboard")` returns null because:
- `Type.GetType("KitchenSink.StatusDashboard")` fails (not in a loaded assembly)
- Searching `AppDomain.CurrentDomain.GetAssemblies()` fails (not loaded)
- The `namespaces` fallback only tries System.Windows.Forms, System.Drawing, System.ComponentModel, System

The control is silently skipped, leaving a blank rectangle where StatusDashboard should appear.

InProcessFormRenderer and CompiledFormRenderer handle this correctly because they compile the whole project (or reference its DLL), so `KitchenSink.StatusDashboard` is a real compiled type.

### Problem 3: Custom Control Design-Time Rendering

For `MoodRing` (overrides `OnPaint`):

- **SyntaxTreeFormRenderer**: Cannot render at all. `ResolveType("KitchenSink.MoodRing")` returns null. Even if it could find the type, SyntaxTreeFormRenderer only sets properties via reflection -- it never triggers `OnPaint`. However, `DrawToBitmap` does trigger `OnPaint`, so if the type were loadable, it would work.
- **InProcessFormRenderer**: Works correctly. It compiles the code and loads DLLs from bin/, which includes the compiled MoodRing. `DrawToBitmap` triggers `OnPaint`.
- **CompiledFormRenderer**: Works correctly. References the project DLL.

---

## A. Top-Level UserControl Rendering

### Detection: Form vs. UserControl

All three renderers need to detect the base type from the .Designer.cs or its companion .cs file. The .Designer.cs itself does NOT declare the base type -- it only says `partial class StatusDashboard`. The base type is in the code-behind:

```csharp
// StatusDashboard.cs
public partial class StatusDashboard : UserControl { ... }
```

**Detection strategy (ordered by reliability):**

1. **Check the companion .cs file** (most reliable). Given `Foo.Designer.cs`, read `Foo.cs` and regex for `: UserControl` or `: Form`.
2. **Heuristic from .Designer.cs**: If the designer code sets `this.Size` but not `this.ClientSize`, and doesn't set `this.Text` or `this.FormBorderStyle`, it's likely a UserControl. This is fragile.
3. **Accept an explicit parameter** from the caller (e.g., `baseType: "UserControl"`).

**Recommended**: Use strategy 1 (check companion file), with strategy 3 as an override, and strategy 2 as a last resort fallback.

### SyntaxTreeFormRenderer Changes

```csharp
// Instead of always: _form = new Form();
// Detect base type and create appropriate container:

private ContainerControl CreateTopLevelContainer(string designerCode, string? designerFilePath)
{
    var baseType = DetectBaseType(designerCode, designerFilePath);
    if (baseType == typeof(UserControl))
    {
        var uc = new UserControl();
        _container = uc;
        // Wrap in a form for DrawToBitmap
        _wrapperForm = new Form();
        _wrapperForm.Controls.Add(uc);
        return uc;
    }
    else
    {
        _form = new Form();
        _container = _form;
        return _form;
    }
}
```

Key insight: `DrawToBitmap` works on any `Control`, not just `Form`. So a UserControl can be rendered directly without wrapping in a Form. However, `Show()` is needed to trigger layout, and `UserControl` doesn't have `Show()`. Solutions:

1. **Host in an invisible Form**: Create a Form, add the UserControl, call `Form.Show()`, then `DrawToBitmap` on the UserControl alone.
2. **Use CreateControl()**: Call `userControl.CreateControl()` which triggers `CreateHandle()` and layout without needing Show(). Then call `DrawToBitmap`.

Option 2 is cleaner:
```csharp
userControl.CreateControl();
var bmp = new Bitmap(userControl.Width, userControl.Height);
userControl.DrawToBitmap(bmp, new Rectangle(0, 0, userControl.Width, userControl.Height));
```

Note: `CreateControl()` requires an STA thread and a message loop may be needed. Testing will confirm.

### InProcessFormRenderer Changes

The generated code-behind must conditionally inherit from `UserControl` or `Form`:

```csharp
private static string GenerateCodeBehind(string? ns, string className,
    List<string> eventHandlers, bool isUserControl)
{
    var baseClass = isUserControl ? "UserControl" : "Form";
    // ...
    sb.AppendLine($"public partial class {className} : {baseClass}");
    // ...
}
```

And `RenderFromAssembly` must handle both:

```csharp
private static byte[] RenderFromAssembly(Assembly assembly, string? ns,
    string className, bool isUserControl)
{
    var fullName = ns != null ? $"{ns}.{className}" : className;
    var type = assembly.GetType(fullName)!;
    var control = (Control)Activator.CreateInstance(type)!;

    if (control is Form form)
    {
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        // ... DrawToBitmap on form ...
        form.Close();
    }
    else
    {
        // UserControl path
        control.CreateControl();
        // ... DrawToBitmap on control ...
    }
    control.Dispose();
}
```

### CompiledFormRenderer Changes

`GenerateCodeBehind` needs the same `isUserControl` parameter. `GenerateProgram` needs to handle the UserControl case:

```csharp
// For UserControl, Program.cs becomes:
var uc = new FullClassName();
uc.CreateControl();
var w = uc.Width > 0 ? uc.Width : 300;
var h = uc.Height > 0 ? uc.Height : 200;
using var bmp = new Bitmap(w, h);
uc.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
uc.Dispose();
// ... output base64 ...
```

No `form.Show()` needed; just `CreateControl()` + `DrawToBitmap`.

---

## B. Recursive UserControl Rendering in SyntaxTreeFormRenderer

### The Problem

When `SyntaxTreeFormRenderer` encounters `new KitchenSink.StatusDashboard()`, `ResolveType` returns null because the type isn't loaded. The control is skipped entirely.

### Approach: Multi-File Syntax Tree Parsing

The renderer should be able to accept multiple .Designer.cs files and parse them recursively:

1. When `ResolveType` fails for a type name like `KitchenSink.StatusDashboard`, check if we have a "project context" -- a directory to search for matching .Designer.cs files.
2. Search for `StatusDashboard.Designer.cs` in the project directory.
3. Parse it recursively, creating a real `UserControl` and running its `InitializeComponent` statements.
4. Return the populated UserControl as the resolved instance.

### Implementation Sketch

```csharp
public class SyntaxTreeFormRenderer
{
    private string? _projectDirectory;  // Set when rendering from file path

    // New: override for ResolveType that tries recursive rendering
    internal Type? ResolveType(string typeName)
    {
        // ... existing logic ...

        // NEW: If standard resolution fails and we have a project context,
        // try to find and parse the .Designer.cs for this type
        if (_projectDirectory != null)
        {
            var instance = TryCreateFromDesignerFile(typeName);
            if (instance != null)
            {
                // Cache the type for future lookups? No -- the type is UserControl.
                // Instead, we need a different approach: override instance creation.
                return instance.GetType(); // This returns UserControl, not StatusDashboard
            }
        }

        return null;
    }
```

Wait -- this won't work cleanly because `ResolveType` returns a `Type`, and `Activator.CreateInstance(typeof(UserControl))` would create a blank UserControl, not one with StatusDashboard's children.

**Better approach: intercept at instance creation, not type resolution.**

```csharp
private object? CreateInstanceForType(string typeName,
    ObjectCreationExpressionSyntax creation)
{
    // First try standard: resolve type and instantiate
    var type = ResolveType(typeName);
    if (type != null)
        return Activator.CreateInstance(type);

    // If that fails, try recursive designer file parsing
    if (_projectDirectory != null)
        return TryCreateFromDesignerFile(typeName);

    return null;
}

private Control? TryCreateFromDesignerFile(string typeName)
{
    // Extract simple class name from fully-qualified name
    // "KitchenSink.StatusDashboard" -> "StatusDashboard"
    var simpleClassName = typeName.Contains('.')
        ? typeName.Substring(typeName.LastIndexOf('.') + 1)
        : typeName;

    // Search for matching .Designer.cs file
    var designerFiles = Directory.GetFiles(_projectDirectory, 
        $"{simpleClassName}.Designer.cs", SearchOption.AllDirectories);
    if (designerFiles.Length == 0) return null;

    // Parse the designer file recursively using a NEW renderer instance
    // (to avoid state conflicts with the parent render)
    var childRenderer = new SyntaxTreeFormRenderer();
    childRenderer._projectDirectory = _projectDirectory;

    var designerCode = File.ReadAllText(designerFiles[0]);
    var tree = CSharpSyntaxTree.ParseText(designerCode);
    var root = tree.GetCompilationUnitRoot();
    var initMethod = root.DescendantNodes()
        .OfType<MethodDeclarationSyntax>()
        .FirstOrDefault(m => m.Identifier.Text == "InitializeComponent");
    if (initMethod?.Body == null) return null;

    // Detect base type
    var baseType = DetectBaseType(designerCode, designerFiles[0]);
    Control container;
    if (baseType == typeof(UserControl))
        container = new UserControl();
    else
        container = new Panel(); // fallback

    childRenderer._fields = new Dictionary<string, object>();
    childRenderer._form = null; // Need to refactor: use _container instead of _form

    // Execute statements with the child container as "this"
    foreach (var statement in initMethod.Body.Statements)
    {
        try { childRenderer.ExecuteStatement(statement); }
        catch { }
    }

    return container;
}
```

### Refactoring Required

The SyntaxTreeFormRenderer currently uses `_form` (type `Form?`) as the "this" reference. For recursive UserControl rendering, this needs to become `_container` (type `Control?`) so it can be either a Form or a UserControl:

```csharp
// Change:
private Form? _form;
// To:
private Control? _container;
```

All references to `_form` in `ResolveTarget`, `ResolveObjectExpression`, `EvaluateExpression` (the `ThisExpressionSyntax` case), and `ExecuteAssignment` need to use `_container` instead. The render methods need to handle both Form and non-Form containers.

### Circular Reference Protection

If UserControl A contains UserControl B which contains UserControl A, we need cycle detection:

```csharp
private HashSet<string> _renderingStack = new();

private Control? TryCreateFromDesignerFile(string typeName)
{
    if (!_renderingStack.Add(typeName))
        return null; // Circular reference, bail out
    try
    {
        // ... recursive rendering ...
    }
    finally
    {
        _renderingStack.Remove(typeName);
    }
}
```

---

## C. Custom Control Design-Time Rendering

### How Visual Studio Renders Custom Controls

Visual Studio's WinForms designer uses this strategy:

1. **Build the project** first (shadow-copy the assembly).
2. Load the compiled assembly into an isolated `AppDomain` (or `AssemblyLoadContext` in .NET Core).
3. Instantiate the control type from the compiled assembly.
4. Call the control's `OnPaint` / `DrawToBitmap` for rendering.
5. If the control has a `[Designer(typeof(MyDesigner))]` attribute, use the `ControlDesigner` class for additional design-time behavior (adorners, smart tags, etc.) -- but the basic rendering is from the control itself.

The `ControlDesigner` class (from `System.Windows.Forms.Design`) provides:
- Design-time adorners (selection handles, snap lines)
- Property filtering
- Smart tags / action lists
- But NOT replacement rendering -- the actual control paint is from the control itself

**Key insight**: There is no "designer proxy" that replaces OnPaint rendering. VS always uses the actual compiled control. The `ControlDesigner` only adds chrome around it.

### Implications for Each Renderer

**InProcessFormRenderer and CompiledFormRenderer**: Already handle this correctly. They compile or reference the project DLL, so MoodRing's `OnPaint` runs during `DrawToBitmap`.

**SyntaxTreeFormRenderer**: Cannot render custom OnPaint without compilation. There is no way to execute `OnPaint` logic from source code via syntax tree parsing -- the code uses `Graphics`, `GraphicsPath`, `PathGradientBrush`, etc. which require actual compiled IL to run.

**Options for SyntaxTreeFormRenderer with custom controls:**

1. **Placeholder rendering** (simplest): When a type can't be resolved, render a colored rectangle with the type name as a label. This is better than a blank space.
   ```csharp
   // When ResolveType fails and recursive parsing also fails:
   var placeholder = new Panel();
   placeholder.BackColor = Color.LightGray;
   placeholder.BorderStyle = BorderStyle.FixedSingle;
   var label = new Label();
   label.Text = typeName;
   label.Dock = DockStyle.Fill;
   label.TextAlign = ContentAlignment.MiddleCenter;
   label.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
   label.ForeColor = Color.DimGray;
   placeholder.Controls.Add(label);
   ```

2. **Hybrid approach**: Try to load the compiled DLL from the project's bin/ directory (like InProcessFormRenderer does) to resolve custom types. If the DLL exists, use the real compiled control. If not, fall back to placeholder.

3. **Do nothing** and rely on users choosing InProcessFormRenderer for forms with custom controls. The tool descriptions already recommend this.

**Recommended**: Implement option 1 (placeholder) as the minimum. Consider option 2 as a fast-follow -- it would make SyntaxTreeFormRenderer nearly as capable as InProcessFormRenderer for custom controls, with the caveat that the project must be built first.

---

## D. Architecture Recommendations

### Should This Be New Tools or Enhancements?

**Enhancements to existing renderers**, not new tools. The three renderers already cover the speed/capability spectrum. Adding more tools would confuse users. Instead:

1. Enhance all three renderers to detect and handle UserControl base types.
2. Enhance SyntaxTreeFormRenderer to support recursive UserControl rendering.
3. Add placeholder rendering for unresolvable types in SyntaxTreeFormRenderer.
4. Optionally add DLL-loading to SyntaxTreeFormRenderer for custom control support.

### Implementation Priority

#### Phase 1: Top-Level UserControl Rendering (All Three Renderers)

Estimated effort: Medium. Well-scoped changes.

1. **Add `DetectBaseType` utility** (shared, perhaps in `CompiledFormRenderer` or a new `DesignerFileHelper`):
   ```csharp
   internal static Type DetectBaseType(string designerFilePath)
   {
       var codeBehindPath = designerFilePath.Replace(".Designer.cs", ".cs");
       if (File.Exists(codeBehindPath))
       {
           var content = File.ReadAllText(codeBehindPath);
           if (Regex.IsMatch(content, @":\s*UserControl\b"))
               return typeof(UserControl);
       }
       return typeof(Form); // default
   }
   ```

2. **SyntaxTreeFormRenderer**: Replace `_form = new Form()` with conditional creation. Refactor `_form` to `_container` (type `Control`). Update `RenderFormToBytes` / `RenderFormToBitmap` to handle UserControl (use `CreateControl()` instead of `Show()`).

3. **InProcessFormRenderer**: Pass `isUserControl` to `GenerateCodeBehind`. Update `RenderFromAssembly` to handle Control (not just Form).

4. **CompiledFormRenderer**: Pass `isUserControl` to `GenerateCodeBehind` and `GenerateProgram`. Generate different Program.cs for UserControl.

5. **Update `ParseDesignerFile`**: Add base type to the return tuple, or add a separate method.

#### Phase 2: Recursive UserControl Rendering (SyntaxTreeFormRenderer)

Estimated effort: Medium-High. Requires refactoring the "this" reference.

1. Refactor `_form` to `_container` (type `Control`).
2. Add `_projectDirectory` field, set from file path.
3. Add `TryCreateFromDesignerFile` method.
4. Intercept instance creation in `ExecuteAssignment` when `ResolveType` returns null.
5. Add circular reference protection.
6. Add placeholder rendering for types that can't be resolved even recursively.

#### Phase 3: Custom Control Hybrid Loading (SyntaxTreeFormRenderer)

Estimated effort: Low-Medium. Mostly borrowing from InProcessFormRenderer.

1. Add optional DLL loading from project bin/ (reuse `ResolveProjectAssemblies` logic from InProcessFormRenderer).
2. When `ResolveType` fails, check loaded assemblies (which now include project DLLs).
3. Fall back to placeholder if still unresolvable.

### File Changes Summary

| File | Phase | Changes |
|---|---|---|
| `SyntaxTreeFormRenderer.cs` | 1 | Refactor `_form` to `_container`, add `DetectBaseType`, update render methods |
| `SyntaxTreeFormRenderer.cs` | 2 | Add `_projectDirectory`, `TryCreateFromDesignerFile`, placeholder rendering |
| `SyntaxTreeFormRenderer.cs` | 3 | Add optional bin/ DLL loading |
| `InProcessFormRenderer.cs` | 1 | Pass `isUserControl` through, update `GenerateCodeBehind` and `RenderFromAssembly` |
| `CompiledFormRenderer.cs` | 1 | Pass `isUserControl` through, update `GenerateCodeBehind`, `GenerateProgram` |
| `CompiledFormRenderer.cs` | 1 | Add `DetectBaseType` static method (shared) |
| `Program.cs` | -- | No changes needed; tool APIs remain the same |
| Tests | 1-3 | Add tests for UserControl rendering, recursive rendering, placeholder rendering |

### Key Risk: CreateControl() Without a Message Loop

`CreateControl()` and `DrawToBitmap()` require a Windows message pump on an STA thread. The current `RenderFormToBytes` in SyntaxTreeFormRenderer already handles this by spawning an STA thread. The same pattern should work for UserControl, but `CreateControl()` may behave differently than `Form.Show()`. This needs testing early.

Fallback if `CreateControl()` alone is insufficient: wrap the UserControl in a hidden Form, call `Form.Show()`, then `DrawToBitmap()` on the UserControl.

```csharp
using var wrapperForm = new Form
{
    ShowInTaskbar = false,
    StartPosition = FormStartPosition.Manual,
    Location = new Point(-32000, -32000),
    ClientSize = new Size(container.Width, container.Height)
};
wrapperForm.Controls.Add(container);
wrapperForm.Show();

var bmp = new Bitmap(container.Width, container.Height);
container.DrawToBitmap(bmp, new Rectangle(0, 0, container.Width, container.Height));
wrapperForm.Close();
```
