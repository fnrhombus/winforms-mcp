# Plan: Visual Studio Designer Parity for Form Rendering

## 1. How Visual Studio Actually Renders Forms

### The Pipeline: Then and Now

When you open a `.Designer.cs` file in Visual Studio and see the form in the designer surface, here is what happens under the hood. The pipeline has evolved significantly between the legacy in-process designer and the modern out-of-process (OOP) designer.

#### The Three Layers to Understand

There are three distinct technologies involved, and conflating them is the source of most confusion:

1. **CodeDom (Code Document Object Model)** -- A language-independent *object model* (`CodeCompileUnit`, `CodeTypeDeclaration`, `CodeAssignStatement`, etc.) that represents code as an abstract tree. This is the *internal representation* used by the designer's serialization infrastructure. It is NOT a parser.

2. **EnvDTE.CodeModel** -- A COM-based interface that VS historically used to *parse source code into CodeDom trees* and to *write CodeDom trees back to source code*. This was the bridge between `.Designer.cs` text and the CodeDom object model. This is what Roslyn replaced.

3. **Roslyn (.NET Compiler Platform)** -- The modern replacement for EnvDTE.CodeModel. Starting with VS 2022 v17.5, Roslyn handles parsing source code and generating source code for the OOP designer.

#### Legacy Pipeline (VS 2019 and earlier, .NET Framework in-process designer)

```
.Designer.cs text
    |
    v
[EnvDTE.CodeModel] -- COM-based, main-thread-only, private VS parser
    |
    v
CodeCompileUnit (CodeDom object model -- language-independent AST)
    |
    v
[CodeDomDesignerLoader.PerformLoad]
    |
    v
[TypeCodeDomSerializer.Deserialize] -- walks CodeDom tree, creates real instances
    |
    v
IDesignerHost with live Form + Controls
    |
    v
DesignSurface.View -- real WinForms Control tree, renderable
    |
    v
DrawToBitmap() -- pixel-perfect output
```

Key characteristics of the legacy pipeline:
- `EnvDTE.CodeModel` was a COM object accessible only from the main thread
- It understood a restricted subset of C# -- specifically the patterns the designer generates
- `CSharpCodeProvider.Parse()` was never the parser; it throws `NotImplementedException` in modern .NET
- The CodeDom object model was both the intermediate representation AND the serialization format
- `TypeCodeDomSerializer.Deserialize()` walked CodeDom nodes to create real control instances

#### Modern Pipeline (VS 2022 v17.5+, .NET OOP designer)

Starting with **Visual Studio 2022 v17.5 Preview 3** (late 2022), the out-of-process designer replaced `EnvDTE.CodeModel` with **Roslyn** for design-time serialization and deserialization.

```
.Designer.cs text
    |
    v
[Roslyn SyntaxTree parsing] -- replaces EnvDTE.CodeModel
    |
    v
CodeDom object model (still used as internal representation)
    |
    v
[TypeCodeDomSerializer.Deserialize] -- same deserialization infrastructure
    |
    v
IDesignerHost with live Form + Controls (in DesignToolsServer.exe)
    |
    v
DesignSurface.View -- rendered and projected to VS client
    |
    v
DrawToBitmap() -- pixel-perfect output
```

**What changed**: Roslyn replaced `EnvDTE.CodeModel` as the mechanism for reading and writing source code. The CodeDom *object model* (`CodeCompileUnit`, etc.) is still used as the internal representation that the serialization/deserialization infrastructure operates on.

**What did NOT change**: `TypeCodeDomSerializer`, `CodeDomDesignerLoader`, `CodeDomSerializer`, and the rest of the deserialization infrastructure remain in use. They still walk CodeDom trees to create component instances. The CodeDom classes themselves are very much alive in `System.Windows.Forms.Design.dll` (see the [dotnet/winforms source](https://github.com/dotnet/winforms/tree/main/src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization)).

#### Why Microsoft Made the Switch

From the [official documentation](https://github.com/dotnet/winforms/blob/main/docs/designer/modernization-of-code-behind-in-OOP-designer/modernization-of-code-behind-in-oop-designer.md):

1. **Threading**: `EnvDTE.CodeModel` was COM-based and accessible only from the main thread. Roslyn unblocks multithreaded serialization and deserialization, solving performance delays in designer load/unload.
2. **Modern language features**: CodeModel was 20+ years old and did not support features like `nameof()`, implicit usings, or modern code style settings.
3. **`.editorconfig` support**: Roslyn-generated code now respects project code style settings (e.g., removing `this.` qualifiers, using simplified type names).
4. **Analyzer compatibility**: Generated code works with modern Roslyn analyzers.

#### Code Generation Changes (Visible Effect)

The Roslyn-based serializer produces modernized `InitializeComponent` code:

```csharp
// Old (EnvDTE.CodeModel, VS 2022 v17.4 and earlier):
this.button1 = new System.Windows.Forms.Button();
this.button1.Location = new System.Drawing.Point(58, 60);

// New (Roslyn, VS 2022 v17.5+):
button1 = new Button();
button1.Location = new Point(58, 60);
```

The new style respects `ImplicitUsings` and `.editorconfig` preferences. This is a one-time code churn when opening a project in VS 17.5+.

#### Scope: OOP Designer Only

**Important**: The Roslyn migration applies only to the **out-of-process designer** (used for .NET Core / .NET 5+ projects, and optionally for .NET Framework projects). The legacy **in-process designer** (still used for some .NET Framework projects) continues to use `EnvDTE.CodeModel`. The in-process designer cannot parse code generated by the modernized OOP serializer (e.g., delegate method group conversions cause issues).

#### Custom CodeDomSerializer Limitation

When loading controls that have custom `CodeDomSerializer` implementations, the OOP designer currently **omits the generated code** in `InitializeComponent` because it cannot run the custom CodeDom serializer the way the legacy pipeline did. This is a known limitation documented on the [dotnet/winforms repo](https://github.com/dotnet/winforms/issues/4790).

---

## 2. The Out-Of-Process Designer (.NET Core / .NET 5+)

### Why It Exists

Visual Studio 2022 is a 64-bit .NET Framework process (`devenv.exe`). It cannot host .NET 8 assemblies in-process. The solution is the **out-of-process (OOP) designer**.

### Architecture

```
VS 2022 (devenv.exe, .NET Framework 4.7.2)
    |
    | JSON-RPC (synchronous)
    v
DesignToolsServer.exe (.NET 8, same bitness as target app)
    |
    v
[Real DesignSurface + IDesignerHost + Controls]
    |
    | Rendered bitmap / proxy state
    v
VS 2022 displays proxy objects on its design surface
```

Key details:
- **DesignToolsServer.exe** runs on the exact .NET version and bitness (x86/x64/ARM64) of the target project
- Real control instances live in the server process
- VS shows **ObjectProxy** instances that mirror the server-side controls
- Rendering is done server-side and projected onto the VS client surface
- All keyboard/mouse input is received client-side and forwarded to the server
- Communication is synchronous JSON-RPC to prevent deadlocks
- **Since VS 2022 v17.5**: The server process uses Roslyn (not EnvDTE.CodeModel) to parse `.Designer.cs` files

### Microsoft.WinForms.Designer.SDK

This NuGet package (`Microsoft.WinForms.Designer.SDK`, latest v1.6.0) is for creating custom **control designers** that work in the OOP designer. It is NOT a general-purpose form rendering library. It provides:
- `Microsoft.DotNet.DesignTools.Designers.ControlDesigner` (replaces `System.Windows.Forms.Design.ControlDesigner`)
- `Microsoft.DotNet.DesignTools.Designers.Actions.DesignerActionList`
- Proxy/server communication infrastructure

**Verdict**: This SDK is for VS extensibility, not for standalone rendering. Not useful for our MCP.

---

## 3. Can We Use DesignSurface Programmatically?

### Availability in .NET 8

**Yes.** Both classes are available in .NET 8:

| Class | Namespace | Assembly | Available |
|-------|-----------|----------|-----------|
| `DesignSurface` | `System.ComponentModel.Design` | `System.Windows.Forms.Design.dll` | .NET 8+ |
| `CodeDomDesignerLoader` | `System.ComponentModel.Design.Serialization` | `System.Windows.Forms.Design.dll` | .NET 8+ |
| `BasicDesignerLoader` | `System.ComponentModel.Design.Serialization` | `System.Windows.Forms.Design.dll` | .NET 8+ |
| `IDesignerHost` | `System.ComponentModel.Design` | `System.Windows.Forms.Design.dll` | .NET 8+ |

These are part of the `net8.0-windows` TFM and don't require extra NuGet packages beyond what a WinForms project already includes.

### The Simple Path: DesignSurface + Type Loading (No CodeDom Parsing)

The simplest way to use DesignSurface programmatically:

```csharp
// Create a DesignSurface and load a Form type
var ds = new DesignSurface();
ds.BeginLoad(typeof(Form));

// Get the IDesignerHost
var host = (IDesignerHost)ds.GetService(typeof(IDesignerHost));

// Create controls programmatically
var button = (Button)host.CreateComponent(typeof(Button));
button.Text = "Hello";
button.Location = new Point(10, 10);
button.Size = new Size(100, 30);
button.Parent = (Form)host.RootComponent;

// Get the rendered view
Control view = (Control)ds.View;

// Render to bitmap
var bmp = new Bitmap(view.Width, view.Height);
view.DrawToBitmap(bmp, new Rectangle(0, 0, view.Width, view.Height));
```

This works, but requires us to **programmatically create components** rather than parse `.Designer.cs` text.

### The Hard Path: CodeDomDesignerLoader (Parse .Designer.cs)

To use `CodeDomDesignerLoader`, you must:

1. **Override `Parse()`** to return a `CodeCompileUnit` from the `.Designer.cs` file
2. **Override `CodeDomProvider`** to provide a `CSharpCodeProvider`
3. **Override `TypeResolutionService`** to resolve types from assemblies
4. **Override `Write()`** for serialization (can be no-op for read-only rendering)

The critical problem: **`CSharpCodeProvider.Parse()` throws `NotImplementedException`**.

Workarounds:
- **Use Roslyn to build a CodeDom tree**: Parse with Roslyn, then manually construct `CodeCompileUnit` / `CodeTypeDeclaration` / `CodeMemberMethod` nodes that mirror the `InitializeComponent()` method. This is what VS itself now does internally (Roslyn parses the source, then builds a CodeDom tree for the serialization infrastructure). However, VS's Roslyn-to-CodeDom bridge is private/internal code within the OOP designer, not a public API we can call.
- **Use a third-party CodeDom parser**: The NRefactory library (from SharpDevelop) had a CodeDom parser, but it's abandoned. The `ICSharpCode.Decompiler` does not produce CodeDom.
- **Skip CodeDom entirely**: Parse with Roslyn, then use `IDesignerHost.CreateComponent()` directly. This bypasses the CodeDom serialization infrastructure but still gets designer-mode rendering benefits.

---

## 4. Assessment: Each Approach vs. VS Parity

### What VS Parity Means

"VS parity" = rendering that produces the same visual output as the VS WinForms designer. This requires:
1. Real control instances (not approximations)
2. Correct property values applied
3. Correct parent-child hierarchy
4. Designer-mode rendering (some controls render differently at design time)
5. Custom control support (loading third-party assemblies)

### Approach Comparison

| Approach | VS Parity | Speed | Complexity | Custom Controls |
|----------|-----------|-------|------------|-----------------|
| **SyntaxTree renderer** (current, Roslyn parse, approximate) | Low (~40%) | ~150ms | Low | No |
| **InProcess renderer** (current, Roslyn parse, create real controls) | Medium (~70%) | ~450ms | Medium | Limited |
| **Compiled renderer** (current, Roslyn compile + execute) | High (~85%) | ~2800ms | Medium | Yes (if referenced) |
| **DesignSurface + manual creation** (parse with Roslyn, create via IDesignerHost) | High (~90%) | ~500-800ms | High | Yes |
| **DesignSurface + Roslyn-to-CodeDom bridge** (build CodeDom from Roslyn, feed to TypeCodeDomSerializer) | Very High (~95%) | ~800-1500ms | Very High | Yes |
| **DesignSurface + compiled type** (compile, then load into DesignSurface) | Highest (~98%) | ~3000-4000ms | Medium | Yes |

Note: The "DesignSurface + Roslyn-to-CodeDom bridge" approach is conceptually what VS itself does internally since v17.5, but VS's bridge code is private. We would have to write our own Roslyn-to-CodeDom translator for the `InitializeComponent()` subset.

### Why DesignSurface Gets Closer to VS

1. **Designer-mode rendering**: Controls with `[Designer]` attributes get their designers loaded. `FormDocumentDesigner` adds the characteristic dotted grid background. Controls render in "design mode" (e.g., `TabControl` shows all tabs).
2. **Proper component siting**: Components are properly sited in a container with `ISite`, which affects how some controls render.
3. **Type resolution**: The designer infrastructure handles type resolution through `ITypeResolutionService`, which is more robust than ad-hoc assembly loading.

### Why 100% VS Parity Is Impractical

1. VS uses a private Roslyn-to-CodeDom bridge to parse `.Designer.cs` files -- this is not available as a public API
2. VS has access to the full project context (referenced assemblies, build output, NuGet packages)
3. The OOP designer has VS-specific services (toolbox, property grid integration, undo engine)
4. Some rendering behaviors depend on the full design-time service stack

---

## 5. Existing Open-Source Implementations

### SharpDevelop (icsharpcode/SharpDevelop)

SharpDevelop implemented a full WinForms designer using the .NET Framework infrastructure:
- Used `CodeDomDesignerLoader` with a custom `IDesignerLoaderProvider`
- Had its own CodeDom parser (NRefactory) that could parse C# into `CodeCompileUnit`
- Created a `DesignSurface`, loaded it with the custom loader, and displayed `DesignSurface.View`
- **Status**: Abandoned, .NET Framework only, NRefactory is no longer maintained

Key file: `src/AddIns/DisplayBindings/FormsDesigner/Project/Src/DesignerViewContent.cs`

### IronmanSoftware C# WinForm Designer (VS Code)

A VS Code extension for WinForms design. Limited documentation on internals. Windows-only, no resource or user control support. Closed source.

### Alternet.Studio FormDesigner

A commercial .NET component that provides a WinForms designer surface. Supports .NET Framework 4.5.2+ and .NET Core 3.1+. Uses `DesignSurface` internally. **Commercial license required.**

### Mono WinForms Designer

Was attempted but never fully completed. Based on the same `DesignSurface` / `IDesignerHost` architecture.

---

## 6. Recommended Path to VS-Like Rendering

### Key Insight: Our Approach Mirrors What VS Does

The discovery that VS itself now uses Roslyn to parse `.Designer.cs` files (replacing the old COM-based `EnvDTE.CodeModel`) validates our proposed approach. The main difference is that VS then bridges Roslyn's output into CodeDom trees for the deserialization infrastructure, while we would skip CodeDom and drive `IDesignerHost.CreateComponent()` directly.

Our proposed `DesignSurfaceFormRenderer` (Roslyn parse -> DesignSurface -> IDesignerHost.CreateComponent) is architecturally similar to what VS does, minus the CodeDom intermediate representation. The CodeDom layer primarily adds value for controls with custom `CodeDomSerializer` implementations -- for standard WinForms controls, the property-assignment patterns are straightforward enough to handle via direct reflection.

### Phase 1: DesignSurface-Based Renderer (New Renderer)

Create a `DesignSurfaceFormRenderer` that:
1. Parses `.Designer.cs` with Roslyn (existing capability)
2. Creates a `DesignSurface` and loads it with `typeof(Form)`
3. Uses `IDesignerHost.CreateComponent()` to create each control found by Roslyn
4. Applies properties via reflection (similar to current InProcessFormRenderer)
5. Calls `DrawToBitmap()` on `DesignSurface.View`

This approach:
- Gets ~90% VS parity (designer-mode rendering, proper siting)
- Estimated ~500-800ms (between InProcess and Compiled)
- Reuses existing Roslyn parsing code
- Does NOT require CodeDom parsing (sidesteps the `CSharpCodeProvider.Parse()` problem)
- Available in .NET 8 with no extra NuGet packages
- Is architecturally aligned with how VS 2022 v17.5+ works (Roslyn-first), just without the CodeDom intermediate layer

### Phase 2: Assembly-Aware Type Resolution

Enhance the renderer with:
- `ITypeResolutionService` implementation that loads project-referenced assemblies
- Shadow copy mechanism for compiled project output
- Support for custom controls (not just System.Windows.Forms types)

### Phase 3: Roslyn-to-CodeDom Bridge (Optional, High Effort)

If higher fidelity is needed, particularly for controls with custom `CodeDomSerializer` implementations:
- Build a Roslyn-to-CodeDom translator for the `InitializeComponent()` subset
- This is conceptually what VS does internally since v17.5 (Roslyn parse -> CodeDom tree -> TypeCodeDomSerializer.Deserialize)
- Implement a custom `CodeDomDesignerLoader` subclass that uses our bridge
- Feed the CodeDom tree through the official deserialization pipeline
- This gets ~95% parity but is significantly more complex
- Note: VS's own Roslyn-to-CodeDom bridge is private/internal, so we would need to write our own

### What NOT To Do

- Do not try to use `Microsoft.WinForms.Designer.SDK` -- it's for VS extensibility, not standalone rendering
- Do not try to use `CSharpCodeProvider.Parse()` -- it throws `NotImplementedException`
- Do not try to use `EnvDTE.CodeModel` -- it's a COM interface tightly coupled to the VS process, and it's being phased out even within VS itself
- Do not try to replicate the full OOP designer architecture -- massive overkill for rendering to bitmap

---

## 7. Code Sketch: DesignSurfaceFormRenderer

```csharp
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Windows.Forms;

/// <summary>
/// Renders a .Designer.cs file using the official DesignSurface infrastructure,
/// producing output equivalent to what VS shows in its designer.
/// </summary>
internal class DesignSurfaceFormRenderer : IDisposable
{
    /// <summary>
    /// Renders a .Designer.cs file to a PNG bitmap.
    /// </summary>
    public byte[] RenderDesignerFile(string designerCsContent)
    {
        // Step 1: Parse with Roslyn (reuse existing SyntaxTree parsing)
        var formInfo = ParseInitializeComponent(designerCsContent);

        // Step 2: Create DesignSurface
        using var surface = new DesignSurface();
        surface.BeginLoad(typeof(Form));

        if (!surface.IsLoaded)
        {
            var errors = surface.LoadErrors?.Cast<object>()
                .Select(e => e.ToString()) ?? Array.Empty<string>();
            throw new InvalidOperationException(
                $"DesignSurface failed to load: {string.Join("; ", errors)}");
        }

        // Step 3: Get IDesignerHost
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        var rootForm = (Form)host.RootComponent;

        // Step 4: Apply root form properties
        ApplyProperties(rootForm, formInfo.FormProperties);

        // Step 5: Create child controls via IDesignerHost
        foreach (var controlInfo in formInfo.Controls)
        {
            var type = ResolveType(controlInfo.TypeName);
            if (type == null) continue;

            // CreateComponent gives proper siting + designer association
            var component = host.CreateComponent(type, controlInfo.Name);

            if (component is Control control)
            {
                ApplyProperties(control, controlInfo.Properties);
                control.Parent = FindParent(rootForm, controlInfo.ParentName);
            }
        }

        // Step 6: Render the designer view to bitmap
        var view = (Control)surface.View;

        // The View includes designer chrome (selection, adorners).
        // For a clean render, use the root form directly.
        var width = Math.Max(rootForm.ClientSize.Width, 100);
        var height = Math.Max(rootForm.ClientSize.Height, 100);

        using var bitmap = new Bitmap(width + 8, height + 30); // + chrome
        view.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

        // Step 7: Encode to PNG
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private Type? ResolveType(string typeName)
    {
        // Try System.Windows.Forms first
        var type = Type.GetType($"System.Windows.Forms.{typeName}, System.Windows.Forms");
        if (type != null) return type;

        // Try fully qualified
        type = Type.GetType(typeName);
        return type;
    }

    // ... ParseInitializeComponent, ApplyProperties, FindParent
    // These reuse existing Roslyn-based parsing from FormRenderer.cs

    public void Dispose()
    {
        // Cleanup if needed
    }
}
```

### Required Package References

```xml
<!-- Already in the project for WinForms -->
<TargetFramework>net8.0-windows</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>

<!-- Needed for DesignSurface, IDesignerHost, etc. -->
<!-- This is a FrameworkReference, automatically included with net8.0-windows + UseWindowsForms -->
<!-- No additional NuGet packages required! -->

<!-- For Roslyn parsing (already referenced) -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
```

The key insight: `System.Windows.Forms.Design.dll` is automatically available when targeting `net8.0-windows` with `UseWindowsForms`. You may need to add:

```xml
<UseWindowsForms>true</UseWindowsForms>
```

to the `.csproj` if not already present, or explicitly reference the assembly.

### Key Differences from Current InProcessFormRenderer

| Aspect | InProcessFormRenderer | DesignSurfaceFormRenderer |
|--------|----------------------|--------------------------|
| Control creation | `Activator.CreateInstance()` | `IDesignerHost.CreateComponent()` |
| Component siting | None | Full ISite with services |
| Designer association | None | Each control gets its designer |
| Design-time rendering | Runtime mode | Design mode (affects TabControl, etc.) |
| Form chrome | Manual approximation | Designer provides it |
| Error handling | Try-catch per control | DesignSurface.LoadErrors |
| Estimated speed | ~450ms | ~500-800ms |
| VS parity | ~70% | ~90% |

---

## 8. Open Questions

1. **Does `DesignSurface.View.DrawToBitmap()` work headlessly?** -- It should work without displaying the form, but needs validation. WinForms controls can be created and rendered off-screen. May need `Application.EnableVisualStyles()` and a message pump.

2. **Thread safety** -- `DesignSurface` must be used on an STA thread. The MCP server currently runs on a thread pool. May need `[STAThread]` or `SynchronizationContext`.

3. **Performance of DesignSurface creation** -- Creating a `DesignSurface` involves initializing many services. Caching/reusing surfaces for multiple renders could improve throughput.

4. **UseWindowsForms in csproj** -- The current project has `<TargetFramework>net8.0-windows</TargetFramework>` but may not have `<UseWindowsForms>true</UseWindowsForms>`. Check whether `System.Windows.Forms.Design.dll` is available.

5. **Non-visual components** -- Components like `Timer`, `ToolTip`, `BindingSource` need to be created via `CreateComponent` but not added to the control tree. The designer handles these via `ComponentTray`.

6. **Modern vs. legacy InitializeComponent syntax** -- Our Roslyn parser needs to handle both the legacy style (`this.button1 = new System.Windows.Forms.Button()`) and the modern Roslyn-generated style (`button1 = new Button()`) since projects opened in VS 2022 v17.5+ will have the modernized code.

---

## 9. Timeline of VS Designer Changes

| VS Version | Date | Change |
|------------|------|--------|
| VS 2019 | 2019 | In-process designer only, .NET Framework, CodeModel + CodeDom |
| VS 2019 16.6+ | 2020 | Early preview of OOP designer for .NET Core |
| VS 2022 17.0 | Nov 2021 | OOP designer GA for .NET 6, still using EnvDTE.CodeModel |
| VS 2022 17.5 Preview 3 | Late 2022 | **OOP designer switches from EnvDTE.CodeModel to Roslyn** |
| VS 2022 17.5 GA | Feb 2023 | Roslyn-based serialization generally available |
| VS 2022 17.9 Preview 2 | Late 2023 | Designer Selection feature for .NET Framework OOP designer |
| VS 2022 17.x | 2024 | Continued OOP designer improvements, .NET 9 Roslyn analyzers |
| VS 2022 17.x+ | 2025+ | Ongoing: custom CodeDomSerializer compatibility improvements |

---

## Sources

- [Modernization of Code-Behind in WinForms OOP Designer (dotnet/winforms)](https://github.com/dotnet/winforms/blob/main/docs/designer/modernization-of-code-behind-in-OOP-designer/modernization-of-code-behind-in-oop-designer.md) -- **Primary source** for the CodeModel-to-Roslyn migration
- [Updated Modern Code Generation for WinForm's InitializeComponent (.NET Blog)](https://devblogs.microsoft.com/dotnet/winforms-codegen-update/)
- [Designer Selection for .NET Framework Projects (dotnet/winforms)](https://github.com/dotnet/winforms/blob/main/docs/designer/designer-selection.md)
- [DesignSurface API Reference (.NET 8)](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.design.designsurface?view=windowsdesktop-8.0)
- [CodeDomDesignerLoader API Reference](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.design.serialization.codedomdesignerloader?view=windowsdesktop-9.0)
- [CodeDomDesignerLoader Source (dotnet/winforms)](https://github.com/dotnet/winforms/blob/main/src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization/CodeDomDesignerLoader.cs)
- [TypeCodeDomSerializer Source (dotnet/winforms)](https://github.com/dotnet/winforms/blob/main/src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization/TypeCodeDomSerializer.cs)
- [Create and Host Custom Designers (MSDN Magazine 2006)](https://learn.microsoft.com/en-us/archive/msdn-magazine/2006/march/create-and-host-custom-designers-with-the-net-framework-2-0)
- [Custom Controls for WinForms Out-Of-Process Designer (.NET Blog)](https://devblogs.microsoft.com/dotnet/custom-controls-for-winforms-out-of-process-designer/)
- [State of the Windows Forms Designer (.NET Blog)](https://devblogs.microsoft.com/dotnet/state-of-the-windows-forms-designer-for-net-applications/)
- [Designer Changes from .NET Framework (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls-design/designer-differences-framework?view=netdesktop-8.0)
- [SharpDevelop FormsDesigner Source (GitHub)](https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/FormsDesigner/Project/Src/DesignerViewContent.cs)
- [WinForms Designer 64-bit Strategy (.NET Blog)](https://devblogs.microsoft.com/dotnet/winforms-designer-64-bit-path-forward/)
- [Microsoft.WinForms.Designer.SDK (NuGet)](https://www.nuget.org/packages/Microsoft.WinForms.Designer.SDK)
- [WinForms Designer Extensibility (GitHub)](https://github.com/microsoft/winforms-designer-extensibility)
- [Custom CodeDomSerializer Issue in .NET 5+ (dotnet/winforms #4790)](https://github.com/dotnet/winforms/issues/4790)
- [Modernized CodeDom Serialization Templates (dotnet/winforms #8350)](https://github.com/dotnet/winforms/issues/8350)
