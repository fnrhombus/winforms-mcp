# Plan: Cross-Framework Form Rendering via Out-of-Process Host

## Problem Statement

The Rhombus.WinFormsMcp MCP server runs on .NET 8. Its `DesignSurfaceFormRenderer` (and `InProcessFormRenderer`) use `DesignSurface`, `Activator.CreateInstance`, and `Assembly.LoadFrom` to instantiate WinForms controls and render them to PNG. This works perfectly for .NET 8 projects, but **cannot render custom controls from .NET Framework 4.x projects** because:

1. .NET 8 cannot load .NET Framework assemblies (`BadImageFormatException` or silent type mismatch)
2. Types like `MainMenu`, `ToolBar`, `StatusBar`, `DataGrid` were removed from .NET Core and don't exist in the .NET 8 runtime
3. Type identity differs across runtimes (a `System.Windows.Forms.Button` from net48 is not the same type as in net8.0)

**Constraint**: The solution must be a **single MCP server binary** that handles any target framework transparently. Requiring users to configure different MCP server binaries per project is unacceptable.

---

## Research: Visual Studio's Out-of-Process Designer

### Architecture Overview

Visual Studio solved an analogous problem: VS itself runs on .NET Framework 4.7.2, but needs to design .NET Core/.NET 5+ WinForms apps. The solution is the **out-of-process (OOP) designer**, introduced with .NET Core 3.1 support.

**Two-process model:**
- **Client Process**: Visual Studio (`devenv.exe`, .NET Framework 4.7.2) -- hosts the designer UI, property grid, toolbox
- **Server Process**: `DesignToolsServer.exe` (.NET 6/7/8/9 matching the target app) -- instantiates real controls, renders them, performs CodeDom serialization

The real controls live exclusively in the DesignToolsServer process. Rendering is done server-side and the bitmap is projected onto VS's design surface. All keyboard/mouse input is captured client-side and transmitted to the server.

### Communication Protocol

- **Transport**: Named pipes with GUID-based pipe names (format: `DesignToolsServer.{GUID}`)
- **Protocol**: JSON-RPC via [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc) (the same library used by C# Dev Kit / Roslyn LSP)
- **Communication pattern**: Synchronous/blocking -- each client-to-server call blocks until the server completes

For each remote procedure, three classes are defined:
1. **Request class** (Protocol project, .NET Standard) -- transports data TO the server
2. **Response class** (Protocol project) -- returns results to client
3. **Handler class** (Server project) -- server-side RPC endpoint

### Proxy Architecture

For each component/control on the design surface:
- A **real .NET object** lives in DesignToolsServer (actual control instance in the target runtime)
- An **ObjectProxy** lives in the VS client process (a .NET Framework proxy representing the server object)

This enables VS (.NET Framework) to reference types that only exist in .NET 8+ (e.g., `TextBox.PlaceholderText`).

### Key Files and Locations

- **DesignToolsServer.exe location**: `C:\Program Files\Microsoft Visual Studio\2022\<Edition>\Common7\IDE\CommonExtensions\Microsoft\Windows.Forms\DesignToolsServer\`
- Subdirectories: `Common\` and `x64\` (or `x86\`, `ARM64\` for bitness matching)
- VS copies required files to a shadow cache folder before launching the server
- Configuration files: `CraftMonitor.designer.deps.json`, `CraftMonitor.designer.runtimeconfig.json`

### NuGet Packages

| Package | Purpose |
|---------|---------|
| `Microsoft.WinForms.Designer.SDK` (v1.6.0) | Base classes for custom control designers (`ControlDesigner`, `DesignerActionList`, etc.) |
| `Microsoft.DotNet.DesignTools.Designers` | Namespace for migrated designer types |
| `Microsoft.DotNet.DesignTools.Designers.Actions` | Action list types |
| `Microsoft.DotNet.DesignTools.Designers.Behaviors` | Adorner/snapline types |
| `Microsoft.WinForms.DesignTools.Protocol` | Protocol/transport classes for JSON-RPC communication |
| `Microsoft.WinForms.DesignTools.Client` | VS-side client libraries |

NuGet package folder structure for OOP designer:
```
lib\<tfm>\                           # Runtime assemblies
lib\<tfm>\Design\WinForms\           # VS-side (client) designers
lib\<tfm>\Design\WinForms\Server\    # DesignToolsServer-side designers
```

### Key Namespaces

| .NET Framework | .NET (OOP Designer) |
|---------------|---------------------|
| `System.Windows.Forms.Design` | `Microsoft.DotNet.DesignTools.Designers` |
| `System.ComponentModel.Design` | `Microsoft.DotNet.DesignTools.Designers.Actions` |
| `System.Windows.Forms.Design.Behavior` | `Microsoft.DotNet.DesignTools.Designers.Behaviors` |

Sources:
- [Custom Controls for WinForm's Out-Of-Process Designer (.NET Blog)](https://devblogs.microsoft.com/dotnet/custom-controls-for-winforms-out-of-process-designer/)
- [Designers changes from .NET Framework (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls-design/designer-differences-framework)
- [WinForms Designer SDK FAQ (GitHub Discussion #7073)](https://github.com/dotnet/winforms/discussions/7073)
- [WinForms Designer Extensibility (GitHub)](https://github.com/microsoft/winforms-designer-extensibility)
- [Control Library NuGet Package Spec](https://github.com/microsoft/winforms-designer-extensibility/blob/main/docs/sdk/control-library-nuget-package-spec.md)
- [State of the Windows Forms Designer (.NET Blog)](https://devblogs.microsoft.com/dotnet/state-of-the-windows-forms-designer-for-net-applications/)

---

## Feasibility Assessment: Using VS Designer Infrastructure Directly

### Can we spawn DesignToolsServer.exe ourselves?

**Verdict: No. This is not viable.**

Reasons:
1. **VS-coupled**: DesignToolsServer.exe is shipped as part of Visual Studio, not as a standalone redistributable. It requires VS to be installed.
2. **No public API**: The server's launch mechanism, pipe name negotiation, and initialization protocol are internal to VS. There is no documented way to spawn it outside `devenv.exe`.
3. **Shadow copy mechanism**: VS uses a "CraftMonitor" system that copies designer assemblies to a temp folder, generates runtime config files, and manages the server lifecycle. This is all internal plumbing.
4. **Protocol is undocumented**: While we know it's JSON-RPC over named pipes via StreamJsonRpc, the actual RPC method names, request/response schemas, and handshake protocol are not documented.
5. **Rendering API is not exposed**: Even if we could spawn the server, there's no documented RPC method for "render this form to a bitmap." The rendering is deeply integrated with VS's design surface infrastructure.
6. **Licensing**: DesignToolsServer.exe is part of Visual Studio and subject to VS licensing terms. Redistributing or depending on it would be legally problematic.

### Can we use the Designer SDK packages programmatically?

**Verdict: No, not for our use case.**

The `Microsoft.WinForms.Designer.SDK` package is designed for **control library authors** who want to provide custom designer experiences within VS. It provides base classes for custom `ControlDesigner`, `DesignerActionList`, and `TypeEditor` implementations. It does not provide:
- A way to host a design surface outside VS
- A rendering API
- A standalone design tools server

The SDK assumes VS is the host and DesignToolsServer is already running.

---

## Feasibility Assessment: Alternative Approaches

### Why cross-runtime assembly loading is impossible

.NET Framework 4.x and .NET Core/.NET 5+ are fundamentally different runtimes:
- Different assembly identity (mscorlib.dll vs System.Runtime.dll)
- Different type forwarding chains
- Different assembly load infrastructure (GAC vs NuGet package cache vs shared framework)
- `AssemblyLoadContext` in .NET 8 cannot load .NET Framework assemblies
- No "shim" or compatibility layer exists

**This is not a solvable problem within a single process.** Cross-framework rendering requires a separate process running the correct runtime.

### Approach: Self-Contained Renderer Host (Recommended)

Instead of trying to reuse VS infrastructure, we build our own lightweight equivalent: a small "renderer host" executable that runs on the target framework and communicates with the .NET 8 MCP server via IPC.

---

## Recommended Architecture: Dual-Runtime Renderer Host

### Design Principles

1. **Single MCP server binary** -- the .NET 8 server is the only thing users configure
2. **Auto-detect target framework** -- read the project's csproj to determine TFM
3. **Spawn the right host** -- if TFM is net4x, spawn a .NET Framework renderer host; if net8.0, render in-process
4. **Simple IPC** -- named pipes with a minimal JSON protocol (not full JSON-RPC, just request/response)
5. **Ship the Framework host alongside the MCP server** -- as a separate exe in the NuGet/NPM package

### Component Overview

```
+---------------------------------------------------+
|  MCP Server (.NET 8)                               |
|                                                    |
|  1. Receive render_form request                    |
|  2. Find .csproj, read TargetFramework             |
|  3. If net8.0-compatible: render in-process        |
|  4. If net4x: spawn FrameworkRendererHost.exe      |
|     - Send designer code + assembly paths via pipe |
|     - Receive PNG bytes back via pipe              |
|  5. Return PNG to MCP client                       |
+---------------------------------------------------+
        |                            |
        | (in-process)               | (named pipe IPC)
        v                            v
+------------------+   +--------------------------------+
| DesignSurface    |   | FrameworkRendererHost.exe       |
| (.NET 8)         |   | (.NET Framework 4.8)            |
| Renders net8.0   |   |                                 |
| controls         |   | 1. Load project assemblies      |
+------------------+   | 2. Create DesignSurface         |
                        | 3. Parse & execute designer code|
                        | 4. DrawToBitmap -> PNG           |
                        | 5. Send PNG bytes back via pipe  |
                        +--------------------------------+
```

### Phase 1: Target Framework Detection

**Already partially implemented** in `FormRenderingHelpers.FindCsproj`. Extend it:

```csharp
public static class FormRenderingHelpers {
    /// <summary>
    /// Read the TargetFramework(s) from a .csproj file.
    /// Returns the best TFM for rendering.
    /// </summary>
    public static string DetectTargetFramework(string csprojPath) {
        var doc = XDocument.Load(csprojPath);

        // Check TargetFrameworks (plural) first
        var tfms = doc.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(tfms)) {
            // Prefer net8.0-windows > net7.0 > net6.0 > net48
            var frameworks = tfms.Split(';');
            return PickBestFramework(frameworks);
        }

        // Single TargetFramework
        var tfm = doc.Descendants("TargetFramework").FirstOrDefault()?.Value;
        return tfm ?? "net8.0-windows"; // Default
    }

    public static bool IsNetFramework(string tfm) {
        // net48, net472, net461, net452, etc.
        return tfm.StartsWith("net4", StringComparison.OrdinalIgnoreCase)
            && !tfm.Contains(".");
    }
}
```

**File**: `src/Rhombus.WinFormsMcp.Server/Automation/FormRenderingHelpers.cs`

### Phase 2: Framework Renderer Host Executable

A minimal .NET Framework 4.8 console application that:
1. Reads a render request from stdin (or named pipe)
2. Loads assemblies from specified paths
3. Creates a `DesignSurface`, parses InitializeComponent via Roslyn, executes statements
4. Calls `DrawToBitmap` and writes PNG bytes to stdout (or named pipe)

**Project**: `src/Rhombus.WinFormsMcp.FrameworkHost/Rhombus.WinFormsMcp.FrameworkHost.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <OutputType>Exe</OutputType>
    <UseWindowsForms>true</UseWindowsForms>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
  </ItemGroup>
</Project>
```

**IPC Protocol** (stdin/stdout, JSON-delimited):

Request (MCP server -> Framework host):
```json
{
  "designerCode": "... InitializeComponent content ...",
  "companionCode": "... optional companion .cs ...",
  "assemblyPaths": ["C:\\path\\to\\CustomControl.dll", ...],
  "width": 0,
  "height": 0
}
```

Response (Framework host -> MCP server):
```json
{
  "success": true,
  "pngBase64": "iVBORw0KGgo...",
  "error": null,
  "skippedTypes": ["ThirdParty.SpecialControl"]
}
```

**Why stdin/stdout over named pipes**: Simpler. No pipe name negotiation. The parent process owns the child's stdin/stdout streams. Named pipes add complexity for zero benefit in this synchronous request-response scenario.

### Phase 3: Transparent Dispatch in DesignSurfaceFormRenderer

Modify the main renderer to auto-detect framework and dispatch:

```csharp
public byte[] RenderForm(string sourceFilePath) {
    var designerFile = FormRenderingHelpers.ResolveDesignerFile(sourceFilePath);
    var csprojPath = FormRenderingHelpers.FindCsproj(
        Path.GetDirectoryName(designerFile)!);
    var tfm = FormRenderingHelpers.DetectTargetFramework(csprojPath);

    if (FormRenderingHelpers.IsNetFramework(tfm)) {
        return RenderViaFrameworkHost(designerFile, csprojPath);
    }

    // Existing in-process rendering for .NET 6/7/8/9+
    return RenderInProcess(designerFile);
}

private byte[] RenderViaFrameworkHost(string designerFile, string csprojPath) {
    var hostExePath = FindFrameworkHost();
    var request = BuildRenderRequest(designerFile, csprojPath);

    using var process = new Process {
        StartInfo = new ProcessStartInfo {
            FileName = hostExePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    process.Start();

    // Send request as single JSON line
    process.StandardInput.WriteLine(JsonSerializer.Serialize(request));
    process.StandardInput.Close();

    // Read response
    var responseJson = process.StandardOutput.ReadToEnd();
    process.WaitForExit(timeout: 15000);

    var response = JsonSerializer.Deserialize<RenderResponse>(responseJson);
    if (!response.Success)
        throw new InvalidOperationException(
            $"Framework renderer failed: {response.Error}");

    return Convert.FromBase64String(response.PngBase64);
}
```

**File**: `src/Rhombus.WinFormsMcp.Server/Automation/DesignSurfaceFormRenderer.cs`

### Phase 4: Packaging

The Framework host exe must be bundled alongside the MCP server:

**NuGet package layout:**
```
tools/
  net8.0-windows/
    Rhombus.WinFormsMcp.Server.exe        # Main MCP server
    Rhombus.WinFormsMcp.Server.dll
    ...
  framework-host/
    Rhombus.WinFormsMcp.FrameworkHost.exe  # .NET Framework 4.8 host
    Rhombus.WinFormsMcp.FrameworkHost.dll
    Microsoft.CodeAnalysis.CSharp.dll
    ...
```

**NPM package layout:**
```
bin/
  Rhombus.WinFormsMcp.Server.exe
  framework-host/
    Rhombus.WinFormsMcp.FrameworkHost.exe
    ...
```

The MCP server locates the framework host relative to its own exe:
```csharp
private string FindFrameworkHost() {
    var serverDir = AppContext.BaseDirectory;
    var hostPath = Path.Combine(serverDir, "framework-host",
        "Rhombus.WinFormsMcp.FrameworkHost.exe");

    if (!File.Exists(hostPath))
        throw new InvalidOperationException(
            "Framework renderer host not found. " +
            "Cannot render .NET Framework projects without it. " +
            $"Expected at: {hostPath}");

    return hostPath;
}
```

---

## Implementation Details

### Shared Code Strategy

The `DesignSurfaceFormRenderer` logic (Roslyn parsing, statement execution, property setting, type resolution) is nearly identical between the .NET 8 in-process path and the Framework host. Options:

1. **Copy the code**: Simplest. The Framework host gets a copy of the renderer logic. Maintenance cost is real but manageable since this code changes infrequently.

2. **Shared source files via MSBuild**: Use `<Compile Include="..\Shared\*.cs" />` to share source files between the two projects without a shared assembly. Both projects compile the same source but against their respective runtimes.

3. **.NET Standard shared library**: Create a `Rhombus.WinFormsMcp.Rendering.Shared` project targeting `netstandard2.0`. Problem: `System.Windows.Forms` and `System.ComponentModel.Design` are not available on .NET Standard. The Roslyn parsing logic could be shared, but not the WinForms rendering logic.

**Recommendation**: Option 2 (shared source files). Extract the Roslyn parsing and statement execution logic into shared `.cs` files. The DesignSurface setup and bitmap capture differ between runtimes but are small.

### Framework Host Lifecycle

Two options for managing the framework host process:

**Option A: One-shot process (simple)**
- Spawn a new process for each render request
- Process starts, renders, outputs result, exits
- ~500ms overhead per render (process start + .NET Framework JIT)
- No state management, no leaks, simple error handling

**Option B: Long-lived process (fast)**
- Spawn once, keep alive, send multiple requests
- ~50ms per render after warmup
- Requires heartbeat/watchdog, error recovery, graceful shutdown
- More complex but much better for batch rendering

**Recommendation**: Start with Option A. The 500ms overhead is acceptable for an MCP tool call (users expect ~1-3s latency). Switch to Option B only if profiling shows the overhead is problematic.

### Error Handling

The Framework host must handle:
- Missing assemblies (custom control DLL not built yet)
- Type resolution failures (third-party control not in assembly search paths)
- Rendering timeouts (control constructor hangs)
- STA thread requirements (WinForms must run on STA thread)

All errors should be returned as structured JSON responses, never as unhandled exceptions that crash the host process.

### Testing Strategy

1. **Unit tests for TFM detection**: Mock csproj files with various `TargetFramework`/`TargetFrameworks` values
2. **Unit tests for dispatch logic**: Verify that `IsNetFramework` correctly identifies net4x TFMs
3. **Integration tests for Framework host** (in a separate test project targeting net48):
   - Render a simple Form with standard controls
   - Render a Form with a custom UserControl
   - Handle missing type gracefully
4. **End-to-end tests**: MCP server renders a net48 project's designer file via the framework host

---

## What This Does NOT Solve

1. **Custom controls from packages not in bin/**: If the user hasn't built the project, there are no assemblies to load. This is a pre-existing limitation regardless of framework.

2. **.NET Core 3.1 / .NET 5 projects**: These need their own runtime versions. The framework host only covers net4x. For .NET 5/6/7 projects, the .NET 8 in-process renderer is close enough (API surface is very similar). A future enhancement could add a "dotnet host" that spawns `dotnet exec` with a specific runtime version.

3. **Non-WinForms UI frameworks**: WPF, MAUI, Avalonia, etc. are out of scope.

4. **Linux/macOS**: .NET Framework 4.x only runs on Windows. The framework host is Windows-only, which matches WinForms' Windows-only nature.

---

## Alternative Approaches Considered and Rejected

### A. Use DesignToolsServer.exe directly
**Rejected**: VS-coupled, no public API, undocumented protocol, licensing concerns. See detailed analysis above.

### B. Multiple MCP server builds (net48 + net8.0)
**Rejected**: Violates the single-binary constraint. Forces users to configure different MCP server binaries per project type.

### C. AnyCPU / Assembly Binding Redirects
**Rejected**: AnyCPU only affects CPU architecture (x86 vs x64), not runtime version. Binding redirects only work within a single runtime version (e.g., redirecting 4.7.2 -> 4.8 within .NET Framework). They cannot bridge .NET Framework to .NET 8.

### D. .NET Standard shared assembly
**Rejected**: WinForms types (`Form`, `Button`, `DesignSurface`) are not available on .NET Standard. A shared assembly could only contain the Roslyn parsing logic, not the rendering logic.

### E. Use Mono / Wine for Framework compatibility
**Rejected**: Mono's WinForms implementation is incomplete and renders differently. Not a viable path for pixel-accurate form preview.

### F. IL rewriting / Type forwarding shims
**Rejected**: Theoretically possible to create type-forwarding assemblies that redirect net48 types to net8.0 equivalents. In practice, the API surface differences are too large (removed types, changed signatures, different default values). The effort would be enormous and fragile.

### G. Compile net48 designer code to .NET 8 with polyfill types
**Rejected**: We'd need to create stub implementations of every removed type (`MainMenu`, `ToolBar`, etc.) that look correct when rendered. This is essentially reimplementing those controls, which is not feasible.

---

## Effort Estimate

| Phase | Description | Effort | Priority |
|-------|-------------|--------|----------|
| 1 | TFM detection in FormRenderingHelpers | 2 hours | High |
| 2 | FrameworkRendererHost.exe (new project) | 8-12 hours | High |
| 3 | Dispatch logic in DesignSurfaceFormRenderer | 4 hours | High |
| 4 | Packaging (NuGet + NPM) | 4 hours | High |
| 5 | Tests | 6 hours | High |
| 6 | Long-lived host process (Option B) | 8 hours | Low (future) |
| **Total** | | **24-28 hours** | |

---

## Prerequisites

- .NET Framework 4.8 Developer Pack must be installed on the build machine
- CI pipeline must support multi-framework builds (build both net8.0-windows and net48 projects)
- The published NuGet/NPM package must include the Framework host exe and its dependencies

---

## Open Questions

1. **Should the Framework host support .NET Framework versions older than 4.8?** Most net4x WinForms projects target 4.7.2 or 4.8. A net48 host can load net472/net461 assemblies via binding redirects. Probably sufficient to target only net48.

2. **Should we pool Framework host processes?** For batch rendering (e.g., rendering all forms in a project), spawning one process per form is wasteful. A pool or long-lived host (Option B) would be better. Defer to Phase 6.

3. **How to handle the Framework host on machines without .NET Framework 4.8?** Modern Windows 10/11 ships with .NET Framework 4.8 pre-installed, so this should rarely be an issue. If missing, return a clear error message suggesting installation.

4. **Should we also support .NET Core 3.1 / .NET 5 / .NET 6 / .NET 7 via separate hosts?** The .NET 8 in-process renderer handles these adequately since the API surface is nearly identical. A separate host would only be needed for custom controls that use APIs removed between versions, which is rare. Defer unless user demand emerges.

---

## Files to Create

| File | Framework | Purpose |
|------|-----------|---------|
| `src/Rhombus.WinFormsMcp.FrameworkHost/Rhombus.WinFormsMcp.FrameworkHost.csproj` | net48 | Project file for Framework renderer host |
| `src/Rhombus.WinFormsMcp.FrameworkHost/Program.cs` | net48 | Entry point: read request from stdin, render, write response to stdout |
| `src/Rhombus.WinFormsMcp.FrameworkHost/FrameworkRenderer.cs` | net48 | DesignSurface-based rendering (shared source with main renderer) |
| `tests/Rhombus.WinFormsMcp.FrameworkHost.Tests/` | net48 | Unit/integration tests for the Framework host |

## Files to Modify

| File | Change |
|------|--------|
| `src/Rhombus.WinFormsMcp.Server/Automation/FormRenderingHelpers.cs` | Add `DetectTargetFramework`, `IsNetFramework`, `PickBestFramework` |
| `src/Rhombus.WinFormsMcp.Server/Automation/DesignSurfaceFormRenderer.cs` | Add framework dispatch logic, `RenderViaFrameworkHost`, `FindFrameworkHost` |
| `src/Rhombus.WinFormsMcp.Server/Program.cs` | No changes needed (renderer API stays the same) |
| `Rhombus.WinFormsMcp.sln` | Add FrameworkHost project and test project |
| `.github/workflows/ci-dev.yml` | Build Framework host as part of CI |
| `src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj` | Add build step to include Framework host in output |
