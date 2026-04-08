using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// fnWindowsMCP - MCP Server for WinForms Automation
///
/// This server provides tools for automating WinForms applications in a headless manner.
/// It communicates via JSON-RPC over stdio (compatible with Claude Code).
/// </summary>
class Program
{
    private static AutomationServer? _server;

    static async Task Main(string[] args)
    {
        try
        {
            var headlessEnv = Environment.GetEnvironmentVariable("HEADLESS");
            var headless = !string.Equals(headlessEnv, "false", StringComparison.OrdinalIgnoreCase)
                        && headlessEnv != "0";

            _server = new AutomationServer(headless);
            await _server.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}

/// <summary>
/// Session manager for tracking automation contexts and element references
/// </summary>
class SessionManager
{
    private readonly Dictionary<string, AutomationElement> _elementCache = new();
    private readonly Dictionary<int, object> _processContext = new();
    private int _nextElementId = 1;
    private AutomationHelper? _automation;
    private readonly bool _headless;

    public SessionManager(bool headless = false)
    {
        _headless = headless;
    }

    public AutomationHelper GetAutomation()
    {
        return _automation ??= new AutomationHelper(_headless);
    }

    public string CacheElement(AutomationElement element)
    {
        var id = $"elem_{_nextElementId++}";
        _elementCache[id] = element;
        return id;
    }

    public AutomationElement? GetElement(string elementId)
    {
        return _elementCache.TryGetValue(elementId, out var elem) ? elem : null;
    }

    public void ClearElement(string elementId)
    {
        _elementCache.Remove(elementId);
    }

    public void CacheProcess(int pid, object context)
    {
        _processContext[pid] = context;
    }

    public void Dispose()
    {
        _automation?.Dispose();
    }
}

/// <summary>
/// Core MCP server implementation handling JSON-RPC communication
/// </summary>
class AutomationServer
{
    private readonly Dictionary<string, Func<JsonElement, Task<JsonElement>>> _tools;
    private int _nextId = 1;
    private readonly SessionManager _session;
    private readonly SyntaxTreeFormRenderer _formRenderer = new();
    private readonly InProcessFormRenderer _inProcessSyntaxTreeFormRenderer = new();
    private readonly CompiledFormRenderer _compiledSyntaxTreeFormRenderer = new();

    public AutomationServer(bool headless = false)
    {
        _session = new SessionManager(headless);
        _tools = new Dictionary<string, Func<JsonElement, Task<JsonElement>>>
        {
            // Element Tools
            { "find_element", FindElement },
            { "click_element", ClickElement },
            { "type_text", TypeText },
            { "set_value", SetValue },
            { "get_property", GetProperty },

            // Process Tools
            { "launch_app", LaunchApp },
            { "attach_to_process", AttachToProcess },
            { "close_app", CloseApp },
            { "get_process_status", GetProcessStatus },

            // Validation Tools
            { "take_screenshot", TakeScreenshot },
            { "element_exists", ElementExists },
            { "wait_for_element", WaitForElement },

            // Interaction Tools
            { "drag_drop", DragDrop },
            { "send_keys", SendKeys },
            { "select_item", SelectItem },
            { "click_menu_item", ClickMenuItem },

            // Form Preview Tools
            { "render_form", RenderForm },
            { "render_form_inprocess", RenderFormInProcess },
            { "render_form_compiled", RenderFormCompiled },

            // Discovery Tools
            { "get_element_tree", GetElementTree },

            // Event Tools
            { "raise_event", RaiseEvent },
            { "listen_for_event", ListenForEvent },
        };
    }

    public async Task RunAsync()
    {
        var reader = Console.In;
        // Use raw stdout stream with explicit LF to avoid Windows CRLF (\r\n),
        // which breaks Node.js JSON parsing when it splits on \n and sees trailing \r.
        var stdoutStream = Console.OpenStandardOutput();
        var writer = new System.IO.StreamWriter(stdoutStream, new System.Text.UTF8Encoding(false))
        {
            NewLine = "\n",
            AutoFlush = false
        };

        // Process incoming messages — wait for client to send initialize first
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var request = JsonDocument.Parse(line).RootElement;

                // Notifications have no "id" — must never send a response
                bool isNotification = !request.TryGetProperty("id", out _);
                if (isNotification)
                {
                    await ProcessNotification(request);
                    continue;
                }

                var requestId = GetRequestId(request);
                var response = await ProcessRequest(request, requestId);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                await writer.FlushAsync();
            }
            catch (Exception ex)
            {
                var error = new
                {
                    jsonrpc = "2.0",
                    id = (object?)null,
                    error = new
                    {
                        code = -32603,
                        message = "Internal error",
                        data = new { details = ex.Message }
                    }
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(error));
                await writer.FlushAsync();
            }
        }
    }

    private static object GetRequestId(JsonElement request)
    {
        if (!request.TryGetProperty("id", out var id))
            return 0;
        return id.ValueKind == JsonValueKind.String ? (object)id.GetString()! : id.GetInt32();
    }

    private Task ProcessNotification(JsonElement request)
    {
        // Notifications are fire-and-forget; no response allowed.
        // "initialized" is the only one we currently receive.
        return Task.CompletedTask;
    }

    private async Task<object> ProcessRequest(JsonElement request, object requestId)
    {
        if (!request.TryGetProperty("method", out var methodElement))
            throw new InvalidOperationException("Missing method");

        var method = methodElement.GetString();
        if (method == "initialize")
        {
            return new
            {
                jsonrpc = "2.0",
                id = requestId,
                result = new
                {
                    protocolVersion = "2024-11-05",
                    // Per MCP spec, capabilities.tools is an empty object {},
                    // not the tools list — tools are fetched via tools/list.
                    capabilities = new
                    {
                        tools = new { }
                    },
                    serverInfo = new
                    {
                        name = "fnWindowsMCP",
                        version = "1.0.0"
                    }
                }
            };
        }

        if (method == "tools/list")
        {
            return new
            {
                jsonrpc = "2.0",
                id = requestId,
                result = new
                {
                    tools = GetToolDefinitions()
                }
            };
        }

        if (method == "tools/call")
        {
            if (!request.TryGetProperty("params", out var paramsElement))
                throw new InvalidOperationException("Missing params");

            if (!paramsElement.TryGetProperty("name", out var nameElement))
                throw new InvalidOperationException("Missing tool name");

            var toolName = nameElement.GetString() ?? throw new InvalidOperationException("Tool name is empty");
            var toolArgs = paramsElement.TryGetProperty("arguments", out var args) ? args : default;

            if (!_tools.ContainsKey(toolName))
                throw new InvalidOperationException($"Unknown tool: {toolName}");

            var result = await _tools[toolName](toolArgs);

            // If the tool returned image data, respond with an MCP image content block
            if (result.TryGetProperty("imageBase64", out var imgData) && imgData.ValueKind == JsonValueKind.String)
            {
                return new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    result = new
                    {
                        content = new object[]
                        {
                            new Dictionary<string, string>
                            {
                                ["type"] = "image",
                                ["data"] = imgData.GetString()!,
                                ["mimeType"] = "image/png"
                            }
                        }
                    }
                };
            }

            return new
            {
                jsonrpc = "2.0",
                id = requestId,
                result = new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = result.ToString()
                        }
                    }
                }
            };
        }

        throw new InvalidOperationException($"Unknown method: {method}");
    }

    private object GetToolDefinitions()
    {
        return new object[]
        {
            new
            {
                name = "find_element",
                description = "Find a UI element by AutomationId, Name, ClassName, or ControlType",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "AutomationId of the element" },
                        name = new { type = "string", description = "Name of the element" },
                        className = new { type = "string", description = "ClassName of the element" },
                        controlType = new { type = "string", description = "ControlType of the element" },
                        parent = new { type = "string", description = "Parent element path (optional)" }
                    }
                }
            },
            new
            {
                name = "click_element",
                description = "Click on a UI element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string", description = "Path or identifier of the element" },
                        doubleClick = new { type = "boolean", description = "Double-click if true" }
                    },
                    required = new[] { "elementPath" }
                }
            },
            new
            {
                name = "type_text",
                description = "Type text into a text field",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string", description = "Path or identifier of the element" },
                        text = new { type = "string", description = "Text to type" },
                        clearFirst = new { type = "boolean", description = "Clear field before typing" }
                    },
                    required = new[] { "elementPath", "text" }
                }
            },
            new
            {
                name = "get_property",
                description = "Get a property or UIA pattern value from a cached UI element. " +
                    "Supported property names: " +
                    "name, automationId, className, controlType, isOffscreen, isEnabled, " +
                    "value (or text) — reads ValuePattern.Value (falls back to Name), " +
                    "isChecked (or toggleState) — reads TogglePattern.ToggleState, " +
                    "isSelected — reads SelectionItemPattern.IsSelected, " +
                    "selectedItem — reads first SelectionPattern.Selection item name, " +
                    "items — JSON array of child item names, " +
                    "itemCount — count of child items, " +
                    "boundingRectangle — JSON {x, y, width, height}, " +
                    "isExpanded — reads ExpandCollapsePattern state, " +
                    "min / max / current — reads RangeValuePattern values (for sliders, NumericUpDown, etc.).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID (e.g. elem_1)" },
                        propertyName = new { type = "string", description = "Property name to read (see tool description for supported names)" }
                    },
                    required = new[] { "elementId", "propertyName" }
                }
            },
            new
            {
                name = "launch_app",
                description = "Launch a WinForms application",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Path to the executable" },
                        arguments = new { type = "string", description = "Command-line arguments (optional)" },
                        workingDirectory = new { type = "string", description = "Working directory (optional)" }
                    },
                    required = new[] { "path" }
                }
            },
            new
            {
                name = "get_process_status",
                description = "Get the current status of a launched process. Use after launch_app to verify the application started successfully, " +
                    "detect crashes (hasExited + exitCode), check responsiveness (responding), or read stderr output for error diagnostics. " +
                    "Returns isRunning, hasExited, exitCode (if exited), responding (Process.Responding), mainWindowTitle, and captured stderr.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID returned by launch_app or attach_to_process" }
                    },
                    required = new[] { "pid" }
                }
            },
            new
            {
                name = "take_screenshot",
                description = "Take a screenshot of the application or element. Returns the image as base64 data that Claude can see directly. Optionally saves to disk if outputPath is provided.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        outputPath = new { type = "string", description = "Path to save the screenshot (optional). If omitted, captures to a temp file, converts to base64, and deletes the temp file." },
                        elementPath = new { type = "string", description = "Specific element to screenshot (optional)" }
                    }
                }
            },
            // ── Form Preview Tools ──
            // Three renderers ranked by speed. Choose based on whether the form uses custom/third-party controls:
            //   render_form           → ~150ms, standard controls only (fastest, but skips custom controls)
            //   render_form_inprocess → ~450ms, all controls including custom/third-party (best balance)
            //   render_form_compiled  → ~2800ms, all controls (slowest, uses dotnet build externally)
            // If the form only uses System.Windows.Forms controls, use render_form.
            // If the form uses custom or third-party controls, use render_form_inprocess.
            // Only fall back to render_form_compiled if render_form_inprocess fails.
            new
            {
                name = "render_form",
                description = "FASTEST (~150ms) — Render a WinForms .Designer.cs to PNG by parsing the syntax tree and creating controls via reflection. " +
                    "PROS: No build step needed; reads the .Designer.cs file directly; fastest turnaround for iterating on layout changes. " +
                    "CONS: Only supports standard System.Windows.Forms and System.Drawing types — custom controls, UserControls, and third-party controls are silently skipped (blank space in output). Event wireups and resource references are ignored. " +
                    "USE WHEN: The form only uses standard WinForms controls and you want the fastest possible preview after editing the designer file. " +
                    "DO NOT USE WHEN: The form contains custom controls, UserControls from the same project, or third-party NuGet controls — use render_form_inprocess instead. " +
                    "IMPORTANT — AUTHORING FORMS: You MUST write forms using the standard Visual Studio designer file convention. " +
                    "Every form requires THREE files: (1) MyForm.cs — the code-behind partial class inheriting Form with constructor calling InitializeComponent(), " +
                    "(2) MyForm.Designer.cs — the designer partial class containing InitializeComponent() with all control creation, property assignments, and layout code, " +
                    "(3) MyForm.resx — resource file (optional, only if embedding images/icons). " +
                    "The .Designer.cs file MUST follow the exact Visual Studio pattern: use 'this.' prefix for all member access, " +
                    "fully-qualified type names (e.g., System.Windows.Forms.Button not just Button), " +
                    "SuspendLayout()/ResumeLayout(false)/PerformLayout() calls, " +
                    "field declarations at the bottom of the class, and a Dispose method with components container. " +
                    "Do NOT put control creation or layout code in the .cs file — it belongs ONLY in .Designer.cs. " +
                    "Do NOT use file-scoped namespaces in designer files — use block-scoped 'namespace X { }' or the traditional Visual Studio style.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        designerFilePath = new { type = "string", description = "Path to the .Designer.cs file" },
                        outputPath = new { type = "string", description = "Optional path to also save the rendered PNG to disk. The image is always returned as base64 regardless." }
                    },
                    required = new[] { "designerFilePath" }
                }
            },
            new
            {
                name = "render_form_inprocess",
                description = "RECOMMENDED (~450ms) — Render a WinForms form to PNG by compiling the designer code in-memory with Roslyn and running it in-process. " +
                    "PROS: Supports ALL controls including custom UserControls and third-party NuGet controls (loaded from the project's build output). Runs real InitializeComponent() code, so rendering is pixel-perfect. No external dotnet build process — 6x faster than render_form_compiled. Returns image as base64. Results cached by content hash. " +
                    "CONS: Requires the project to have been built at least once (so custom control DLLs exist in bin/). Slightly slower than render_form for standard-only forms. " +
                    "USE WHEN: The form uses custom controls, UserControls, or third-party controls. Also the safe default choice when you're unsure what control types are used. " +
                    "DO NOT USE WHEN: You need the absolute fastest preview and you know the form only has standard controls — use render_form instead. " +
                    "IMPORTANT — AUTHORING FORMS: You MUST write forms using the standard Visual Studio designer file convention. " +
                    "Every form requires a SEPARATE .Designer.cs file — do NOT put InitializeComponent() in the main .cs file. " +
                    "The .Designer.cs MUST contain: a partial class declaration, a private IContainer components field, a Dispose(bool) override, " +
                    "and an InitializeComponent() method that creates controls with fully-qualified type names (e.g., 'this.button1 = new System.Windows.Forms.Button();'), " +
                    "sets all properties using 'this.' prefix, calls SuspendLayout()/ResumeLayout(false)/PerformLayout(), " +
                    "and declares all control fields at the bottom of the class. " +
                    "The companion .cs file should ONLY contain the partial class with a constructor calling InitializeComponent() plus event handlers. " +
                    "This is exactly how Visual Studio generates WinForms code — follow this pattern precisely or rendering will fail.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        sourceFilePath = new { type = "string", description = "Path to the .cs or .Designer.cs file. If given a .cs file, automatically finds the sibling .Designer.cs." }
                    },
                    required = new[] { "sourceFilePath" }
                }
            },
            new
            {
                name = "render_form_compiled",
                description = "SLOWEST (~2800ms) — Render a WinForms form to PNG by generating a temporary .csproj, running dotnet build, and capturing the output. " +
                    "PROS: Most robust — generates a full .NET project with PackageReferences copied from the source .csproj, so it handles any control type. Works even if the project has never been built. References the project's built assembly for custom controls. " +
                    "CONS: Slowest option (~2.8s per render) due to spawning dotnet build + a separate process. " +
                    "USE WHEN: render_form_inprocess fails (e.g. missing build output, complex project configurations, or exotic package setups). Also useful as a one-time validation that the designer code is correct. " +
                    "DO NOT USE WHEN: Speed matters — use render_form_inprocess instead for iterative development. " +
                    "IMPORTANT — AUTHORING FORMS: You MUST write forms using the standard Visual Studio designer file convention with a SEPARATE .Designer.cs file. " +
                    "See render_form or render_form_inprocess descriptions for the full authoring requirements. " +
                    "The .Designer.cs file is the input to ALL renderers — if it doesn't follow the Visual Studio pattern, rendering will fail or produce incorrect output.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        sourceFilePath = new { type = "string", description = "Path to the .cs or .Designer.cs file. If given a .cs file, automatically finds the sibling .Designer.cs." }
                    },
                    required = new[] { "sourceFilePath" }
                }
            },
            new
            {
                name = "select_item",
                description = "Select an item in a combo box, list box, or similar selection control. " +
                    "Handles the expand-find-select pattern automatically: expands the control (if collapsible), " +
                    "finds the target item by text value or zero-based index, selects it via SelectionItemPattern " +
                    "(or falls back to ScrollIntoView + click), and collapses the control. " +
                    "Provide either 'value' (text match, case-insensitive) or 'index' (zero-based position). " +
                    "Returns the text of the selected item.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID of the selection control (combo box, list box, etc.)" },
                        value = new { type = "string", description = "Text of the item to select (case-insensitive match). Provide this or index." },
                        index = new { type = "integer", description = "Zero-based index of the item to select. Provide this or value." }
                    },
                    required = new[] { "elementId" }
                }
            },
            new
            {
                name = "click_menu_item",
                description = "Navigate and click a menu item in a menu bar or context menu. " +
                    "Provide a menuPath array of menu item names forming the navigation path (e.g. [\"File\", \"Save As\"]). " +
                    "For each level, the tool finds the menu item by name, expands it (via ExpandCollapsePattern or click), " +
                    "waits for the submenu to appear, and then proceeds to the next level. The final item is invoked " +
                    "(via InvokePattern) or clicked. Works with both MenuBar items and context menu items. " +
                    "Optionally provide a pid to scope the search to a specific application window.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        menuPath = new { type = "array", description = "Array of menu item names forming the navigation path, e.g. [\"File\", \"Save As\"]", items = new { type = "string" } },
                        pid = new { type = "integer", description = "Optional process ID to scope the search to a specific window" }
                    },
                    required = new[] { "menuPath" }
                }
            },
            new
            {
                name = "get_element_tree",
                description = "Get the UI automation element tree as structured JSON. Returns a recursive tree of elements with name, controlType, automationId, boundingRectangle, isEnabled, isOffscreen, and children. " +
                    "Each discovered element is cached and assigned an elementId for subsequent tool calls (click_element, type_text, etc.). " +
                    "Use this tool to discover the UI structure of a running application before interacting with specific elements.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID — uses the process main window as root" },
                        elementId = new { type = "string", description = "Cached element ID to use as root (from a previous find_element or get_element_tree call)" },
                        depth = new { type = "integer", description = "Maximum depth to traverse (default 3)" },
                        maxElements = new { type = "integer", description = "Maximum total elements to include (default 50)" }
                    }
                }
            }
        };
    }

    // Tool implementations
    private Task<JsonElement> FindElement(JsonElement args)
    {
        try
        {
            var automation = _session.GetAutomation();
            var pid = GetIntArg(args, "pid");
            var automationId = GetStringArg(args, "automationId");
            var name = GetStringArg(args, "name");
            var className = GetStringArg(args, "className");

            AutomationElement? element = null;

            if (!string.IsNullOrEmpty(automationId))
            {
                element = automation.FindByAutomationId(automationId);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                element = automation.FindByName(name);
            }
            else if (!string.IsNullOrEmpty(className))
            {
                element = automation.FindByClassName(className);
            }

            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found\"}").RootElement);

            var elementId = _session.CacheElement(element);
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"elementId\": \"{elementId}\", \"name\": \"{element.Name ?? ""}\", \"automationId\": \"{element.AutomationId ?? ""}\", \"controlType\": \"{element.ControlType}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ClickElement(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var doubleClick = GetBoolArg(args, "doubleClick", false);

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.Click(element, doubleClick);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Element clicked\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> TypeText(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var text = GetStringArg(args, "text") ?? "";
            var clearFirst = GetBoolArg(args, "clearFirst", false);

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.TypeText(element, text, clearFirst);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Text typed\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SetValue(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var value = GetStringArg(args, "value") ?? "";

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.SetValue(element, value);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Value set\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetProperty(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var propertyName = GetStringArg(args, "propertyName") ?? "";

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var value = automation.GetProperty(element, propertyName);

            var valueJson = value == null ? "null" : $"\"{EscapeJson(value.ToString())}\"";
            var json = $"{{\"success\": true, \"propertyName\": \"{propertyName}\", \"value\": {valueJson}}}";
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> LaunchApp(JsonElement args)
    {
        try
        {
            var path = GetStringArg(args, "path") ?? throw new ArgumentException("path is required");
            var arguments = GetStringArg(args, "arguments");
            var workingDirectory = GetStringArg(args, "workingDirectory");

            var automation = _session.GetAutomation();
            var process = automation.LaunchApp(path, arguments, workingDirectory);

            _session.CacheProcess(process.Id, process);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"pid\": {process.Id}, \"processName\": \"{process.ProcessName}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> AttachToProcess(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            var processName = GetStringArg(args, "processName");

            var automation = _session.GetAutomation();
            var process = !string.IsNullOrEmpty(processName)
                ? automation.AttachToProcessByName(processName)
                : automation.AttachToProcess(pid);

            _session.CacheProcess(process.Id, process);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"pid\": {process.Id}, \"processName\": \"{process.ProcessName}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> CloseApp(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            var force = GetBoolArg(args, "force", false);

            var automation = _session.GetAutomation();
            automation.CloseApp(pid, force);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Application closed\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetProcessStatus(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");

            var automation = _session.GetAutomation();
            var status = automation.GetProcessStatus(pid);

            var jsonObj = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["isRunning"] = status["isRunning"],
                ["hasExited"] = status["hasExited"],
                ["exitCode"] = status["exitCode"],
                ["responding"] = status["responding"],
                ["mainWindowTitle"] = status["mainWindowTitle"],
                ["stderr"] = status["stderr"]
            };

            var json = JsonSerializer.Serialize(jsonObj);
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> TakeScreenshot(JsonElement args)
    {
        try
        {
            var outputPath = GetStringArg(args, "outputPath");
            var elementId = GetStringArg(args, "elementId");

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            if (!string.IsNullOrEmpty(elementId))
                element = _session.GetElement(elementId!);

            // If no outputPath provided, use a temp file
            var useTempFile = string.IsNullOrEmpty(outputPath);
            var screenshotPath = useTempFile
                ? Path.Combine(Path.GetTempPath(), $"screenshot_{Guid.NewGuid():N}.png")
                : outputPath!;

            automation.TakeScreenshot(screenshotPath, element);

            // Read the file and convert to base64
            var imageBytes = File.ReadAllBytes(screenshotPath);
            var base64 = Convert.ToBase64String(imageBytes);

            // Clean up temp file
            if (useTempFile)
            {
                try { File.Delete(screenshotPath); }
                catch { /* best-effort cleanup */ } // COVERAGE_EXCEPTION: Temp file cleanup race condition
            }

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"imageBase64\": \"{base64}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ElementExists(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");

            var automation = _session.GetAutomation();
            var exists = automation.ElementExists(automationId);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"exists\": {(exists ? "true" : "false")}}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private async Task<JsonElement> WaitForElement(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");
            var timeoutMs = GetIntArg(args, "timeoutMs", 10000);

            var automation = _session.GetAutomation();
            var found = await automation.WaitForElementAsync(automationId, null, timeoutMs);

            return JsonDocument.Parse($"{{\"success\": true, \"found\": {(found ? "true" : "false")}}}").RootElement;
        }
        catch (Exception ex)
        {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
    }

    private Task<JsonElement> DragDrop(JsonElement args)
    {
        try
        {
            var sourceElementId = GetStringArg(args, "sourceElementId") ?? throw new ArgumentException("sourceElementId is required");
            var targetElementId = GetStringArg(args, "targetElementId") ?? throw new ArgumentException("targetElementId is required");

            var sourceElement = _session.GetElement(sourceElementId!);
            var targetElement = _session.GetElement(targetElementId!);

            if (sourceElement == null || targetElement == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Source or target element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.DragDrop(sourceElement, targetElement);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Drag and drop completed\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SendKeys(JsonElement args)
    {
        try
        {
            var keys = GetStringArg(args, "keys") ?? throw new ArgumentException("keys is required");

            var automation = _session.GetAutomation();
            automation.SendKeys(keys);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Keys sent\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> RenderForm(JsonElement args)
    {
        try
        {
            var designerFilePath = GetStringArg(args, "designerFilePath") ?? throw new ArgumentException("designerFilePath is required");
            var outputPath = GetStringArg(args, "outputPath");

            byte[] pngBytes;

            if (!string.IsNullOrEmpty(outputPath))
            {
                // Save to disk AND return base64
                _formRenderer.RenderDesignerFile(designerFilePath, outputPath);
                pngBytes = System.IO.File.ReadAllBytes(outputPath);
            }
            else
            {
                // No output path: render to bytes only
                pngBytes = _formRenderer.RenderDesignerFileToBytes(designerFilePath);
            }

            var base64 = Convert.ToBase64String(pngBytes);
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"imageBase64\": \"{base64}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> RenderFormInProcess(JsonElement args)
    {
        try
        {
            var sourceFilePath = GetStringArg(args, "sourceFilePath") ?? throw new ArgumentException("sourceFilePath is required");

            var pngBytes = _inProcessSyntaxTreeFormRenderer.RenderForm(sourceFilePath);
            var base64 = Convert.ToBase64String(pngBytes);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"imageBase64\": \"{base64}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> RenderFormCompiled(JsonElement args)
    {
        try
        {
            var sourceFilePath = GetStringArg(args, "sourceFilePath") ?? throw new ArgumentException("sourceFilePath is required");

            var pngBytes = _compiledSyntaxTreeFormRenderer.RenderForm(sourceFilePath);
            var base64 = Convert.ToBase64String(pngBytes);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"imageBase64\": \"{base64}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetElementTree(JsonElement args)
    {
        try
        {
            var automation = _session.GetAutomation();
            var pid = GetIntArg(args, "pid");
            var elementId = GetStringArg(args, "elementId");
            var depth = GetIntArg(args, "depth", 3);
            var maxElements = GetIntArg(args, "maxElements", 50);

            AutomationElement? root = null;

            if (!string.IsNullOrEmpty(elementId))
            {
                root = _session.GetElement(elementId!);
                if (root == null)
                    return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);
            }
            else if (pid > 0)
            {
                root = automation.GetMainWindow(pid);
                if (root == null)
                    return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Could not find main window for the specified process\"}").RootElement);
            }

            if (root == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Either pid or elementId must be provided\"}").RootElement);

            var tree = automation.GetElementTree(root, depth, maxElements, el => _session.CacheElement(el));

            var json = JsonSerializer.Serialize(new { success = true, tree, elementCount = tree.Count });
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SelectItem(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var value = GetStringArg(args, "value");
            var index = GetNullableIntArg(args, "index");

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var selectedValue = automation.SelectItem(element, value, index);

            var json = $"{{\"success\": true, \"selectedValue\": \"{EscapeJson(selectedValue)}\"}}";
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ClickMenuItem(JsonElement args)
    {
        try
        {
            var menuPath = GetStringArrayArg(args, "menuPath") ?? throw new ArgumentException("menuPath is required");
            var pid = GetNullableIntArg(args, "pid");

            var automation = _session.GetAutomation();
            automation.ClickMenuItem(menuPath, pid);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> RaiseEvent(JsonElement args)
    {
        // Event raising is handled by FlaUI patterns in future enhancement
        return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Event raising not yet implemented\"}").RootElement);
    }

    private Task<JsonElement> ListenForEvent(JsonElement args)
    {
        // Event listening is handled by FlaUI event handlers in future enhancement
        return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Event listening not yet implemented\"}").RootElement);
    }

    // Helper methods
    private string? GetStringArg(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return null;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.Null
            ? prop.GetString()
            : null;
    }

    private int GetIntArg(JsonElement args, string key, int defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : defaultValue;
    }

    private bool GetBoolArg(JsonElement args, string key, bool defaultValue = false)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.True
            ? true
            : args.TryGetProperty(key, out var prop2) && prop2.ValueKind == JsonValueKind.False
                ? false
                : defaultValue;
    }

    private int? GetNullableIntArg(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return null;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }

    private string[]? GetStringArrayArg(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return null;

        if (!args.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                result.Add(item.GetString()!);
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    private string EscapeJson(string? value)
    {
        if (value == null)
            return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
