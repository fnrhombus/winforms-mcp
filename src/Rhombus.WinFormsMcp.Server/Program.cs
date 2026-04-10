using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FlaUI.Core.AutomationElements;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rhombus.WinFormsMcp.Rendering;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// WinFormsMcp - MCP Server for WinForms Automation
///
/// This server provides tools for automating WinForms applications in a headless manner.
/// It communicates via JSON-RPC over stdio (compatible with Claude Code).
/// </summary>
class Program {
    static async Task Main(string[] args) {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) => {
                services.AddSingleton<IPostConfigureOptions<McpServerOptions>>(
                    new McpServerOptionsConfiguration(context.Configuration));
                services.Configure<McpServerOptions>(context.Configuration);

                services.AddMemoryCache();
                services.AddSingleton<IAutomationHelper>(sp => {
                    var opts = sp.GetRequiredService<IOptions<McpServerOptions>>();
                    return new AutomationHelper(opts.Value.Headless, sp.GetRequiredService<ILogger<AutomationHelper>>());
                });
                services.AddSingleton<ISessionManager, SessionManager>();
                services.AddSingleton<RendererProcessPool>();

                services.AddSingleton(sp => {
                    var opts = sp.GetRequiredService<IOptions<McpServerOptions>>();
                    return opts.Value.TelemetryOptOut
                        ? (ITelemetry)sp.GetRequiredService<NullTelemetry>()
                        : sp.GetRequiredService<Telemetry>();
                });
                services.AddSingleton<NullTelemetry>();
                services.AddSingleton<Telemetry>();

                services.AddHostedService<AutomationServer>();
            })
            .ConfigureLogging((context, logging) => {
                logging.ClearProviders();

                var options = new McpServerOptions();
                var optionsConfig = new McpServerOptionsConfiguration(context.Configuration);
                optionsConfig.PostConfigure(Options.DefaultName, options);

                logging.SetMinimumLevel(options.MinimumLogLevel);
                logging.AddConsole(consoleOptions => {
                    consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace;
                });
            })
            .Build();

        await host.RunAsync();
    }
}

/// <summary>
/// Interface for session management — tracking automation contexts and element references.
/// </summary>
interface ISessionManager : IDisposable {
    IAutomationHelper GetAutomation();
    string CacheElement(AutomationElement element);
    AutomationElement? GetElement(string elementId);
    void ClearElement(string elementId);
    void CacheProcess(int pid, object context);
}

/// <summary>
/// Session manager for tracking automation contexts and element references
/// </summary>
class SessionManager : ISessionManager {
    private readonly Dictionary<string, AutomationElement> _elementCache = new();
    private readonly Dictionary<int, object> _processContext = new();
    private int _nextElementId = 1;
    private readonly IAutomationHelper _automation;

    public SessionManager(IAutomationHelper automation) {
        _automation = automation;
    }

    public IAutomationHelper GetAutomation() {
        return _automation;
    }

    public string CacheElement(AutomationElement element) {
        var id = $"elem_{_nextElementId++}";
        _elementCache[id] = element;
        return id;
    }

    public AutomationElement? GetElement(string elementId) {
        return _elementCache.TryGetValue(elementId, out var elem) ? elem : null;
    }

    public void ClearElement(string elementId) {
        _elementCache.Remove(elementId);
    }

    public void CacheProcess(int pid, object context) {
        _processContext[pid] = context;
    }

    public void Dispose() {
        _automation?.Dispose();
    }
}

/// <summary>
/// Core MCP server implementation handling JSON-RPC communication.
/// Runs as a hosted service — the host starts it and manages its lifetime.
/// </summary>
class AutomationServer : BackgroundService {
    private readonly Dictionary<string, Func<JsonElement, Task<JsonElement>>> _tools;
    private readonly ISessionManager _session;
    private readonly RendererProcessPool _rendererPool;
    private readonly ITelemetry _telemetry;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AutomationServer> _logger;

    public AutomationServer(ISessionManager session, RendererProcessPool rendererPool, ITelemetry telemetry, IHostApplicationLifetime lifetime, ILogger<AutomationServer> logger) {
        _session = session;
        _rendererPool = rendererPool;
        _telemetry = telemetry;
        _lifetime = lifetime;
        _logger = logger;
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

            // Discovery Tools
            { "get_element_tree", GetElementTree },

            // Condition & Toggle Tools
            { "wait_for_condition", WaitForCondition },
            { "toggle_element", ToggleElement },

            // Data & Scroll Tools
            { "scroll_element", ScrollElement },
            { "get_table_data", GetTableData },
            { "set_table_cell", SetTableCell },

            // Window Management Tools
            { "manage_window", ManageWindow },
            { "list_windows", ListWindows },
            { "get_focused_element", GetFocusedElement },

            // Event Tools
            { "raise_event", RaiseEvent },
            { "listen_for_event", ListenForEvent },
            { "open_context_menu", OpenContextMenu },

            // Polish Tools
            { "get_clipboard", GetClipboard },
            { "set_clipboard", SetClipboard },
            { "read_tooltip", ReadTooltip },
            { "find_elements", FindElements },
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _logger.LogInformation("MCP server started, waiting for client connection");
        var reader = Console.In;
        // Use raw stdout stream with explicit LF to avoid Windows CRLF (\r\n),
        // which breaks Node.js JSON parsing when it splits on \n and sees trailing \r.
        var stdoutStream = Console.OpenStandardOutput();
        var writer = new System.IO.StreamWriter(stdoutStream, new System.Text.UTF8Encoding(false)) {
            NewLine = "\n",
            AutoFlush = false
        };

        // Process incoming messages — wait for client to send initialize first
        while (!stoppingToken.IsCancellationRequested) {
            var line = await reader.ReadLineAsync(stoppingToken);
            if (line == null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try {
                var request = JsonDocument.Parse(line).RootElement;

                // Notifications have no "id" — must never send a response
                bool isNotification = !request.TryGetProperty("id", out _);
                if (isNotification) {
                    await ProcessNotification(request);
                    continue;
                }

                var requestId = GetRequestId(request);
                var response = await ProcessRequest(request, requestId);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                await writer.FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error processing request");
                _telemetry.TrackException(ex);
                var error = new {
                    jsonrpc = "2.0",
                    id = (object?)null,
                    error = new {
                        code = -32603,
                        message = "Internal error",
                        data = new { details = ex.Message }
                    }
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(error));
                await writer.FlushAsync(stoppingToken);
            }
        }

        // Stdin closed or cancellation requested — tell the host to shut down
        // so all hosted services stop and DI containers are disposed.
        _logger.LogInformation("MCP server shutting down");
        _rendererPool.Dispose();
        _telemetry.Flush();
        _lifetime.StopApplication();
    }

    private static object GetRequestId(JsonElement request) {
        if (!request.TryGetProperty("id", out var id))
            return 0;
        return id.ValueKind == JsonValueKind.String ? (object)id.GetString()! : id.GetInt32();
    }

    private Task ProcessNotification(JsonElement request) {
        // Notifications are fire-and-forget; no response allowed.
        // "initialized" is the only one we currently receive.
        return Task.CompletedTask;
    }

    private async Task<object> ProcessRequest(JsonElement request, object requestId) {
        if (!request.TryGetProperty("method", out var methodElement))
            throw new InvalidOperationException("Missing method");

        var method = methodElement.GetString();
        if (method == "initialize") {
            _logger.LogInformation("Client initialized");
            return new {
                jsonrpc = "2.0",
                id = requestId,
                result = new {
                    protocolVersion = "2024-11-05",
                    // Per MCP spec, capabilities.tools is an empty object {},
                    // not the tools list — tools are fetched via tools/list.
                    capabilities = new {
                        tools = new { }
                    },
                    serverInfo = new {
                        name = "fnWindowsMCP",
                        version = "1.0.0"
                    }
                }
            };
        }

        if (method == "tools/list") {
            return new {
                jsonrpc = "2.0",
                id = requestId,
                result = new {
                    tools = GetToolDefinitions()
                }
            };
        }

        if (method == "tools/call") {
            if (!request.TryGetProperty("params", out var paramsElement))
                throw new InvalidOperationException("Missing params");

            if (!paramsElement.TryGetProperty("name", out var nameElement))
                throw new InvalidOperationException("Missing tool name");

            var toolName = nameElement.GetString() ?? throw new InvalidOperationException("Tool name is empty");
            var toolArgs = paramsElement.TryGetProperty("arguments", out var args) ? args : default;

            if (!_tools.ContainsKey(toolName))
                throw new InvalidOperationException($"Unknown tool: {toolName}");

            _logger.LogDebug("Executing tool: {ToolName}", toolName);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            JsonElement result;
            try {
                result = await _tools[toolName](toolArgs);
            }
            catch (Exception ex) {
                sw.Stop();
                _logger.LogError(ex, "Tool {ToolName} failed after {ElapsedMs}ms", toolName, sw.ElapsedMilliseconds);
                _telemetry.TrackToolCall(toolName, sw.Elapsed);
                _telemetry.TrackException(ex);
                throw;
            }
            sw.Stop();
            _logger.LogDebug("Tool {ToolName} completed in {ElapsedMs}ms", toolName, sw.ElapsedMilliseconds);
            _telemetry.TrackToolCall(toolName, sw.Elapsed);

            // If the tool returned image data, respond with an MCP image content block
            if (result.TryGetProperty("imageBase64", out var imgData) && imgData.ValueKind == JsonValueKind.String) {
                return new {
                    jsonrpc = "2.0",
                    id = requestId,
                    result = new {
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

            return new {
                jsonrpc = "2.0",
                id = requestId,
                result = new {
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

    private object GetToolDefinitions() {
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
                description = "Take a screenshot of the application or element. Returns the image as base64 data that Claude can see directly. " +
                    "Uses PrintWindow for capture, which works for both visible and headless (hidden desktop) processes. " +
                    "Provide pid for the most reliable capture path. Optionally saves to disk if outputPath is provided.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID — captures the process's main window via PrintWindow (recommended)" },
                        outputPath = new { type = "string", description = "Path to save the screenshot (optional). If omitted, captures to a temp file, converts to base64, and deletes the temp file." },
                        elementPath = new { type = "string", description = "Specific element to screenshot (optional)" }
                    }
                }
            },
            // ── Form Preview Tools ──
            new
            {
                name = "render_form",
                description = "Render a WinForms .Designer.cs file to a pixel-accurate PNG preview using .NET's DesignSurface — the same infrastructure Visual Studio uses for its WYSIWYG designer. " +
                    "Works WITHOUT building the project. Automatically detects the project's target framework and renders in a matching out-of-process host (supports .NET Framework 4.x, .NET Core 3.x, and .NET 5–9+). " +
                    "Returns the image as inline base64 so you can see the form layout directly. Results are cached by content hash for instant re-renders. " +
                    "AUTHORING REQUIREMENTS: Forms must use the standard Visual Studio designer file convention — " +
                    "a separate .Designer.cs file containing a partial class with InitializeComponent(), " +
                    "fully-qualified type names (e.g., System.Windows.Forms.Button), 'this.' prefix for member access, " +
                    "SuspendLayout()/ResumeLayout(false)/PerformLayout() calls, field declarations at the bottom, and a Dispose method with components container. " +
                    "Do NOT put control creation in the .cs file — it belongs ONLY in .Designer.cs.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        designerFilePath = new { type = "string", description = "Path to the .Designer.cs file (or the companion .cs file — the .Designer.cs will be found automatically)" },
                        outputPath = new { type = "string", description = "Optional path to also save the rendered PNG to disk. The image is always returned as base64 regardless." }
                    },
                    required = new[] { "designerFilePath" }
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
            },
            // ── Previously undocumented tools ──
            new
            {
                name = "set_value",
                description = "Set a value on a UI element using UIA ValuePattern. Works on text boxes, combo boxes, and other value-holding controls. " +
                    "Unlike type_text, this sets the value directly without simulating keystrokes — works on hidden desktops.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID (e.g. elem_1)" },
                        value = new { type = "string", description = "Value to set on the element" }
                    },
                    required = new[] { "elementId", "value" }
                }
            },
            new
            {
                name = "attach_to_process",
                description = "Attach to a running process by PID or process name. Use this to automate an application that is already running " +
                    "(e.g., an app launched outside the MCP server). Always attaches on the default (visible) desktop. " +
                    "Provide either pid or processName (not both).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID to attach to" },
                        processName = new { type = "string", description = "Process name to attach to (e.g. \"notepad\"). Finds the first matching process." }
                    }
                }
            },
            new
            {
                name = "close_app",
                description = "Close a running application. By default sends WM_CLOSE for a graceful shutdown. " +
                    "Use force=true to kill the process immediately (Process.Kill).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID of the application to close" },
                        force = new { type = "boolean", description = "If true, forcefully kill the process instead of graceful close (default false)" }
                    },
                    required = new[] { "pid" }
                }
            },
            new
            {
                name = "element_exists",
                description = "Quick boolean check for whether a UI element with the given AutomationId exists. " +
                    "Uses a short 1-second timeout. Use this for fast existence checks before attempting interactions.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "AutomationId of the element to check" }
                    },
                    required = new[] { "automationId" }
                }
            },
            new
            {
                name = "wait_for_element",
                description = "Wait for a UI element to appear, polling at 100ms intervals. " +
                    "Use this after actions that trigger async UI changes (e.g., opening a dialog, loading data). " +
                    "Returns whether the element was found within the timeout period.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "AutomationId of the element to wait for" },
                        timeoutMs = new { type = "integer", description = "Maximum time to wait in milliseconds (default 10000)" }
                    },
                    required = new[] { "automationId" }
                }
            },
            new
            {
                name = "drag_drop",
                description = "Perform a drag-and-drop operation from one element to another. " +
                    "Uses mouse simulation — only works on the default (visible) desktop, not on hidden desktops. " +
                    "Both elements must be cached from a prior find_element or get_element_tree call.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        sourceElementId = new { type = "string", description = "Cached element ID of the drag source" },
                        targetElementId = new { type = "string", description = "Cached element ID of the drop target" }
                    },
                    required = new[] { "sourceElementId", "targetElementId" }
                }
            },
            new
            {
                name = "send_keys",
                description = "Send keyboard input to the application. Uses SendKeys syntax: " +
                    "^ = Ctrl, % = Alt, + = Shift, {ENTER}, {TAB}, {ESC}, {DELETE}, {F1}-{F12}, etc. " +
                    "Examples: \"^a\" (Ctrl+A), \"^c\" (Ctrl+C), \"%{F4}\" (Alt+F4), \"+{TAB}\" (Shift+Tab). " +
                    "Uses input simulation — only works on the default (visible) desktop. " +
                    "For text input on hidden desktops, use type_text or set_value instead.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        keys = new { type = "string", description = "Keys to send using SendKeys syntax" },
                        pid = new { type = "integer", description = "Optional process ID to focus before sending keys" }
                    },
                    required = new[] { "keys" }
                }
            },
            // ── Phase 1: Reliability & Async ──
            new
            {
                name = "wait_for_condition",
                description = "Wait for a UI element's property to match a condition. Polls at 100ms intervals. " +
                    "Use this after actions that trigger async changes — e.g., wait until a label shows 'Done', " +
                    "a button becomes enabled, or a progress bar reaches a value. " +
                    "Property names are the same as get_property (value, isEnabled, isChecked, name, etc.).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID to poll" },
                        propertyName = new { type = "string", description = "Property name to check (same names as get_property)" },
                        expectedValue = new { type = "string", description = "The value to wait for" },
                        comparison = new { type = "string", description = "Comparison type: equals (default), contains, not_equals, greater_than, less_than" },
                        timeoutMs = new { type = "integer", description = "Maximum wait time in milliseconds (default 10000)" }
                    },
                    required = new[] { "elementId", "propertyName", "expectedValue" }
                }
            },
            new
            {
                name = "toggle_element",
                description = "Toggle a checkbox, radio button, or toggle button using UIA TogglePattern. " +
                    "Optionally specify a desired state (on/off/indeterminate) to toggle until that state is reached. " +
                    "Works on hidden desktops. Returns the previous and current toggle states.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID of the toggle control" },
                        desiredState = new { type = "string", description = "Target state: 'on', 'off', or 'indeterminate'. Omit to just toggle once." }
                    },
                    required = new[] { "elementId" }
                }
            },
            // ── Phase 2: Data-Heavy Apps ──
            new
            {
                name = "scroll_element",
                description = "Scroll within a scrollable control (ListBox, DataGridView, TreeView, Panel, etc.) using UIA ScrollPattern. " +
                    "Works on hidden desktops. Returns the current scroll position percentages.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID of the scrollable control" },
                        direction = new { type = "string", description = "Scroll direction: up, down, left, right" },
                        amount = new { type = "integer", description = "Number of scroll units (default 1)" },
                        scrollType = new { type = "string", description = "Scroll granularity: 'line' (default) or 'page'" }
                    },
                    required = new[] { "elementId", "direction" }
                }
            },
            new
            {
                name = "get_table_data",
                description = "Read data from a DataGridView or other grid control. Returns structured JSON with headers, row count, " +
                    "column count, and cell values. Supports pagination via startRow/rowCount for large grids. " +
                    "Works with both WinForms DataGridView and generic UIA Grid controls.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID of the grid/table control" },
                        startRow = new { type = "integer", description = "First row to return (zero-based, default 0)" },
                        rowCount = new { type = "integer", description = "Maximum number of rows to return (default 50)" },
                        columns = new { type = "array", description = "Optional array of column indices to include (default: all)", items = new { type = "integer" } }
                    },
                    required = new[] { "elementId" }
                }
            },
            new
            {
                name = "set_table_cell",
                description = "Set a cell value in a DataGridView or grid control. " +
                    "Navigates to the cell and sets its value via ValuePattern, falling back to click-to-edit. " +
                    "Returns the previous and new values.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID of the grid/table control" },
                        row = new { type = "integer", description = "Zero-based row index" },
                        column = new { type = "integer", description = "Zero-based column index" },
                        value = new { type = "string", description = "Value to set in the cell" }
                    },
                    required = new[] { "elementId", "row", "column", "value" }
                }
            },
            // ── Phase 3: Window & Multi-Form Management ──
            new
            {
                name = "manage_window",
                description = "Manage a window: maximize, minimize, restore, resize, move, or close. " +
                    "Uses UIA WindowPattern and TransformPattern. Works on hidden desktops for state changes; " +
                    "resize/move may have no visible effect on hidden desktops but the state is tracked.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID of the target window" },
                        action = new { type = "string", description = "Window action: maximize, minimize, restore, resize, move, close" },
                        width = new { type = "integer", description = "Target width (for resize action)" },
                        height = new { type = "integer", description = "Target height (for resize action)" },
                        x = new { type = "integer", description = "Target X position (for move action)" },
                        y = new { type = "integer", description = "Target Y position (for move action)" }
                    },
                    required = new[] { "pid", "action" }
                }
            },
            new
            {
                name = "list_windows",
                description = "List all top-level windows for a process. Returns title, className, visibility, controlType, and bounding rectangle " +
                    "for each window. Each window is cached with an elementId for use as parent in find_element or get_element_tree. " +
                    "Useful for apps with multiple forms, dialogs, or MDI windows.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID to enumerate windows for" }
                    },
                    required = new[] { "pid" }
                }
            },
            // ── Phase 4: Event System & Context Menus ──
            new
            {
                name = "raise_event",
                description = "Raise a UIA event on an element. Supported events: " +
                    "invoke (click/activate), toggle (checkbox/toggle button), expand, collapse, " +
                    "select (selection item), scroll_into_view. " +
                    "Note: This triggers UIA patterns directly — it cannot raise arbitrary .NET events.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID to raise the event on" },
                        eventName = new { type = "string", description = "Event to raise: invoke, toggle, expand, collapse, select, scroll_into_view" }
                    },
                    required = new[] { "elementId", "eventName" }
                }
            },
            new
            {
                name = "listen_for_event",
                description = "Listen for a UIA event with a timeout. Registers an event handler and waits for it to fire. " +
                    "Supported event types: focus_changed, structure_changed, property_changed. " +
                    "Returns whether the event fired within the timeout period.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Optional cached element ID to scope the listener to (omit for desktop-wide)" },
                        eventType = new { type = "string", description = "Event type: focus_changed, structure_changed, property_changed" },
                        timeoutMs = new { type = "integer", description = "Maximum wait time in milliseconds (default 10000)" }
                    },
                    required = new[] { "eventType" }
                }
            },
            new
            {
                name = "open_context_menu",
                description = "Open a context menu on an element. " +
                    "On hidden desktops, uses WM_CONTEXTMENU message (works across desktops). " +
                    "On visible desktops, uses mouse right-click. " +
                    "Returns the context menu element cached for use with click_menu_item or find_element.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID to right-click" }
                    },
                    required = new[] { "elementId" }
                }
            },
            new
            {
                name = "get_focused_element",
                description = "Get the UI element that currently has keyboard focus. Returns the element's properties and caches it with an elementId " +
                    "for subsequent interactions. Useful for debugging tab order, verifying focus after interactions, or understanding keyboard navigation.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Optional process ID to verify the focused element belongs to this process" }
                    }
                }
            },
            // ── Phase 5: Polish & Edge Cases ──
            new
            {
                name = "get_clipboard",
                description = "Get the current text content of the Windows clipboard. " +
                    "Runs on an STA thread for COM clipboard access.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "set_clipboard",
                description = "Set the Windows clipboard text content. " +
                    "Runs on an STA thread for COM clipboard access.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string", description = "Text to place on the clipboard" }
                    },
                    required = new[] { "text" }
                }
            },
            new
            {
                name = "read_tooltip",
                description = "Read tooltip or help text from a UI element. " +
                    "Tries HelpText property first, then LegacyIAccessible description, " +
                    "then focuses the element and searches for a ToolTip popup.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID to read tooltip from" }
                    },
                    required = new[] { "elementId" }
                }
            },
            new
            {
                name = "find_elements",
                description = "Find ALL UI elements matching a search criterion (not just the first match). " +
                    "Returns an array of matching elements, each cached with an elementId. " +
                    "Useful for 'find all buttons', 'find all list items', etc.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "AutomationId to search for" },
                        name = new { type = "string", description = "Name to search for" },
                        className = new { type = "string", description = "ClassName to search for" },
                        controlType = new { type = "string", description = "ControlType to search for (e.g. Button, TextBox, ListItem)" },
                        parent = new { type = "string", description = "Optional parent element ID to scope the search" }
                    }
                }
            }
        };
    }

    // Tool implementations
    private Task<JsonElement> FindElement(JsonElement args) {
        try {
            var automation = _session.GetAutomation();
            var pid = GetIntArg(args, "pid");
            var automationId = GetStringArg(args, "automationId");
            var name = GetStringArg(args, "name");
            var className = GetStringArg(args, "className");

            AutomationElement? element = null;

            if (!string.IsNullOrEmpty(automationId)) {
                element = automation.FindByAutomationId(automationId);
            }
            else if (!string.IsNullOrEmpty(name)) {
                element = automation.FindByName(name);
            }
            else if (!string.IsNullOrEmpty(className)) {
                element = automation.FindByClassName(className);
            }

            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found\"}").RootElement);

            var elementId = _session.CacheElement(element);
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"elementId\": \"{elementId}\", \"name\": \"{element.Name ?? ""}\", \"automationId\": \"{element.AutomationId ?? ""}\", \"controlType\": \"{element.ControlType}\"}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ClickElement(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var doubleClick = GetBoolArg(args, "doubleClick", false);

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.Click(element, doubleClick);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Element clicked\"}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> TypeText(JsonElement args) {
        try {
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
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SetValue(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var value = GetStringArg(args, "value") ?? "";

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.SetValue(element, value);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Value set\"}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetProperty(JsonElement args) {
        try {
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
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> LaunchApp(JsonElement args) {
        try {
            var path = GetStringArg(args, "path") ?? throw new ArgumentException("path is required");
            var arguments = GetStringArg(args, "arguments");
            var workingDirectory = GetStringArg(args, "workingDirectory");

            var automation = _session.GetAutomation();
            var process = automation.LaunchApp(path, arguments, workingDirectory);

            _session.CacheProcess(process.Id, process);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"pid\": {process.Id}, \"processName\": \"{process.ProcessName}\"}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> AttachToProcess(JsonElement args) {
        try {
            var pid = GetIntArg(args, "pid");
            var processName = GetStringArg(args, "processName");

            var automation = _session.GetAutomation();
            var process = !string.IsNullOrEmpty(processName)
                ? automation.AttachToProcessByName(processName)
                : automation.AttachToProcess(pid);

            _session.CacheProcess(process.Id, process);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"pid\": {process.Id}, \"processName\": \"{process.ProcessName}\"}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> CloseApp(JsonElement args) {
        try {
            var pid = GetIntArg(args, "pid");
            var force = GetBoolArg(args, "force", false);

            var automation = _session.GetAutomation();
            automation.CloseApp(pid, force);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Application closed\"}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetProcessStatus(JsonElement args) {
        try {
            var pid = GetIntArg(args, "pid");

            var automation = _session.GetAutomation();
            var status = automation.GetProcessStatus(pid);

            var jsonObj = new Dictionary<string, object?> {
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
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> TakeScreenshot(JsonElement args) {
        try {
            var outputPath = GetStringArg(args, "outputPath");
            var elementId = GetStringArg(args, "elementId");
            var pid = GetNullableIntArg(args, "pid");

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            if (!string.IsNullOrEmpty(elementId))
                element = _session.GetElement(elementId!);

            // If no outputPath provided, use a temp file
            var useTempFile = string.IsNullOrEmpty(outputPath);
            var screenshotPath = useTempFile
                ? Path.Combine(Path.GetTempPath(), $"screenshot_{Guid.NewGuid():N}.png")
                : outputPath!;

            automation.TakeScreenshot(screenshotPath, element, pid);

            // Read the file and convert to base64
            var imageBytes = File.ReadAllBytes(screenshotPath);
            var base64 = Convert.ToBase64String(imageBytes);

            // Clean up temp file
            if (useTempFile) {
                try { File.Delete(screenshotPath); }
                catch { /* best-effort cleanup */ } // COVERAGE_EXCEPTION: Temp file cleanup race condition
            }

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"imageBase64\": \"{base64}\"}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ElementExists(JsonElement args) {
        try {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");

            var automation = _session.GetAutomation();
            var exists = automation.ElementExists(automationId);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"exists\": {(exists ? "true" : "false")}}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private async Task<JsonElement> WaitForElement(JsonElement args) {
        try {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");
            var timeoutMs = GetIntArg(args, "timeoutMs", 10000);

            var automation = _session.GetAutomation();
            var found = await automation.WaitForElementAsync(automationId, null, timeoutMs);

            return JsonDocument.Parse($"{{\"success\": true, \"found\": {(found ? "true" : "false")}}}").RootElement;
        }
        catch (Exception ex) {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
    }

    private Task<JsonElement> DragDrop(JsonElement args) {
        try {
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
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SendKeys(JsonElement args) {
        try {
            var keys = GetStringArg(args, "keys") ?? throw new ArgumentException("keys is required");
            var pid = GetNullableIntArg(args, "pid");

            var automation = _session.GetAutomation();
            automation.SendKeys(keys, pid);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Keys sent\"}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private async Task<JsonElement> RenderForm(JsonElement args) {
        try {
            var designerFilePath = GetStringArg(args, "designerFilePath") ?? throw new ArgumentException("designerFilePath is required");
            var outputPath = GetStringArg(args, "outputPath");

            // Resolve the designer file and find the companion .cs
            var designerFile = FormRenderingHelpers.ResolveDesignerFile(designerFilePath);
            var designerContent = System.IO.File.ReadAllText(designerFile);

            var idx = designerFile.LastIndexOf(".Designer.cs", StringComparison.OrdinalIgnoreCase);
            var companionPath = idx >= 0 ? designerFile.Substring(0, idx) + ".cs" : designerFile;
            var companionContent = System.IO.File.Exists(companionPath) ? System.IO.File.ReadAllText(companionPath) : null;

            // Determine TFM: env var override or auto-detect from csproj
            var configuredTfm = _rendererPool.GetConfiguredTfm();
            string? csprojPath = null;
            if (string.Equals(configuredTfm, "auto", StringComparison.OrdinalIgnoreCase)) {
                var dir = Path.GetDirectoryName(designerFile)!;
                csprojPath = FormRenderingHelpers.FindCsproj(dir);
            }

            var pngBytes = await _rendererPool.RenderAsync(
                designerContent, companionContent, null, configuredTfm, csprojPath);

            if (!string.IsNullOrEmpty(outputPath)) {
                System.IO.File.WriteAllBytes(outputPath, pngBytes);
            }

            var base64 = Convert.ToBase64String(pngBytes);
            return JsonDocument.Parse($"{{\"success\": true, \"imageBase64\": \"{base64}\"}}").RootElement;
        }
        catch (Exception ex) {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
    }

    private Task<JsonElement> GetElementTree(JsonElement args) {
        try {
            var automation = _session.GetAutomation();
            var pid = GetIntArg(args, "pid");
            var elementId = GetStringArg(args, "elementId");
            var depth = GetIntArg(args, "depth", 3);
            var maxElements = GetIntArg(args, "maxElements", 50);

            AutomationElement? root = null;

            if (!string.IsNullOrEmpty(elementId)) {
                root = _session.GetElement(elementId!);
                if (root == null)
                    return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);
            }
            else if (pid > 0) {
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
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SelectItem(JsonElement args) {
        try {
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
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ClickMenuItem(JsonElement args) {
        try {
            var menuPath = GetStringArrayArg(args, "menuPath") ?? throw new ArgumentException("menuPath is required");
            var pid = GetNullableIntArg(args, "pid");

            var automation = _session.GetAutomation();
            automation.ClickMenuItem(menuPath, pid);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ScrollElement(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var direction = GetStringArg(args, "direction") ?? throw new ArgumentException("direction is required");
            var amount = GetIntArg(args, "amount", 1);
            var scrollType = GetStringArg(args, "scrollType") ?? "line";

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var result = automation.Scroll(element, direction, amount, scrollType);

            var json = JsonSerializer.Serialize(new Dictionary<string, object>(result) { ["success"] = true });
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetTableData(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var startRow = GetIntArg(args, "startRow", 0);
            var rowCount = GetIntArg(args, "rowCount", 50);
            int[]? columns = null;
            if (args.TryGetProperty("columns", out var colsProp) && colsProp.ValueKind == JsonValueKind.Array) {
                columns = colsProp.EnumerateArray()
                    .Where(c => c.ValueKind == JsonValueKind.Number)
                    .Select(c => c.GetInt32())
                    .ToArray();
            }

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var result = automation.GetTableData(element, startRow, rowCount, columns);
            result["success"] = true;

            var json = JsonSerializer.Serialize(result);
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SetTableCell(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var row = GetIntArg(args, "row");
            var column = GetIntArg(args, "column");
            var value = GetStringArg(args, "value") ?? throw new ArgumentException("value is required");

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var (previousValue, newValue) = automation.SetTableCell(element, row, column, value);

            var prevJson = previousValue == null ? "null" : $"\"{EscapeJson(previousValue)}\"";
            var newJson = newValue == null ? "null" : $"\"{EscapeJson(newValue)}\"";
            return Task.FromResult(JsonDocument.Parse(
                $"{{\"success\": true, \"previousValue\": {prevJson}, \"newValue\": {newJson}}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ManageWindow(JsonElement args) {
        try {
            var pid = GetIntArg(args, "pid");
            var action = GetStringArg(args, "action") ?? throw new ArgumentException("action is required");
            var width = GetNullableIntArg(args, "width");
            var height = GetNullableIntArg(args, "height");
            var x = GetNullableIntArg(args, "x");
            var y = GetNullableIntArg(args, "y");

            var automation = _session.GetAutomation();
            var result = automation.ManageWindow(pid, action, width, height, x, y);
            result["success"] = true;

            var json = JsonSerializer.Serialize(result);
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ListWindows(JsonElement args) {
        try {
            var pid = GetIntArg(args, "pid");

            var automation = _session.GetAutomation();
            var windows = automation.ListWindows(pid);

            // Cache each window element for subsequent operations
            for (int i = 0; i < windows.Count; i++) {
                windows[i]["windowIndex"] = i;
            }

            var result = new Dictionary<string, object?> {
                ["success"] = true,
                ["windowCount"] = windows.Count,
                ["windows"] = windows
            };

            var json = JsonSerializer.Serialize(result);
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetFocusedElement(JsonElement args) {
        try {
            var pid = GetNullableIntArg(args, "pid");

            var automation = _session.GetAutomation();
            var focused = automation.GetFocusedElement();

            if (focused == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"No focused element found\"}").RootElement);

            // If pid specified, verify the focused element belongs to that process
            if (pid.HasValue) {
                try {
                    var focusedPid = focused.Properties.ProcessId.ValueOrDefault;
                    if (focusedPid != pid.Value)
                        return Task.FromResult(JsonDocument.Parse(
                            $"{{\"success\": true, \"focused\": false, \"message\": \"Focused element belongs to process {focusedPid}, not {pid.Value}\"}}").RootElement);
                }
                catch { /* ignore if ProcessId unavailable */ }
            }

            var elementId = _session.CacheElement(focused);
            var rect = focused.BoundingRectangle;

            var result = new Dictionary<string, object?> {
                ["success"] = true,
                ["focused"] = true,
                ["elementId"] = elementId,
                ["name"] = focused.Name,
                ["automationId"] = focused.AutomationId,
                ["className"] = focused.ClassName,
                ["controlType"] = focused.ControlType.ToString(),
                ["boundingRectangle"] = new Dictionary<string, int> {
                    ["x"] = (int)rect.X,
                    ["y"] = (int)rect.Y,
                    ["width"] = (int)rect.Width,
                    ["height"] = (int)rect.Height
                }
            };

            var json = JsonSerializer.Serialize(result);
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private async Task<JsonElement> WaitForCondition(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var propertyName = GetStringArg(args, "propertyName") ?? throw new ArgumentException("propertyName is required");
            var expectedValue = GetStringArg(args, "expectedValue") ?? throw new ArgumentException("expectedValue is required");
            var comparison = GetStringArg(args, "comparison") ?? "equals";
            var timeoutMs = GetIntArg(args, "timeoutMs", 10000);

            var element = _session.GetElement(elementId);
            if (element == null)
                return JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement;

            var automation = _session.GetAutomation();
            var (matched, actualValue, elapsedMs) = await automation.WaitForConditionAsync(
                element, propertyName, expectedValue, comparison, timeoutMs);

            var actualJson = actualValue == null ? "null" : $"\"{EscapeJson(actualValue)}\"";
            return JsonDocument.Parse(
                $"{{\"success\": true, \"matched\": {(matched ? "true" : "false")}, \"actualValue\": {actualJson}, \"elapsedMs\": {elapsedMs}}}").RootElement;
        }
        catch (Exception ex) {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
    }

    private Task<JsonElement> ToggleElement(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var desiredState = GetStringArg(args, "desiredState");

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var (previousState, currentState) = automation.Toggle(element, desiredState);

            return Task.FromResult(JsonDocument.Parse(
                $"{{\"success\": true, \"previousState\": \"{EscapeJson(previousState)}\", \"currentState\": \"{EscapeJson(currentState)}\"}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetClipboard(JsonElement args) {
        try {
            var automation = _session.GetAutomation();
            var text = automation.GetClipboardText();

            var textJson = text == null ? "null" : $"\"{EscapeJson(text)}\"";
            return Task.FromResult(JsonDocument.Parse(
                $"{{\"success\": true, \"text\": {textJson}}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SetClipboard(JsonElement args) {
        try {
            var text = GetStringArg(args, "text") ?? throw new ArgumentException("text is required");

            var automation = _session.GetAutomation();
            automation.SetClipboardText(text);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Clipboard text set\"}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ReadTooltip(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var tooltip = automation.GetTooltipText(element);

            var tooltipJson = tooltip == null ? "null" : $"\"{EscapeJson(tooltip)}\"";
            return Task.FromResult(JsonDocument.Parse(
                $"{{\"success\": true, \"tooltip\": {tooltipJson}}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> FindElements(JsonElement args) {
        try {
            var automationId = GetStringArg(args, "automationId");
            var name = GetStringArg(args, "name");
            var className = GetStringArg(args, "className");
            var controlType = GetStringArg(args, "controlType");
            var parentId = GetStringArg(args, "parent");

            AutomationElement? parent = null;
            if (parentId != null) {
                parent = _session.GetElement(parentId);
                if (parent == null)
                    return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Parent element not found in session\"}").RootElement);
            }

            var automation = _session.GetAutomation();
            var elements = automation.FindAllMatching(automationId, name, className, controlType, parent);

            if (elements == null || elements.Length == 0)
                return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"count\": 0, \"elements\": []}").RootElement);

            var results = new List<Dictionary<string, object?>>();
            foreach (var el in elements) {
                var elId = _session.CacheElement(el);
                results.Add(new Dictionary<string, object?> {
                    ["elementId"] = elId,
                    ["name"] = el.Name,
                    ["automationId"] = el.AutomationId,
                    ["className"] = el.ClassName,
                    ["controlType"] = el.ControlType.ToString()
                });
            }

            var json = JsonSerializer.Serialize(new { success = true, count = results.Count, elements = results });
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> RaiseEvent(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var eventName = GetStringArg(args, "eventName") ?? throw new ArgumentException("eventName is required");

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var result = automation.RaiseEvent(element, eventName);

            return Task.FromResult(JsonDocument.Parse(
                $"{{\"success\": true, \"result\": \"{EscapeJson(result)}\"}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private async Task<JsonElement> ListenForEvent(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId");
            var eventType = GetStringArg(args, "eventType") ?? throw new ArgumentException("eventType is required");
            var timeoutMs = GetIntArg(args, "timeoutMs", 10000);

            AutomationElement? element = null;
            if (elementId != null) {
                element = _session.GetElement(elementId);
                if (element == null)
                    return JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement;
            }

            var automation = _session.GetAutomation();
            var (fired, eventDetails, elapsedMs) = await automation.ListenForEventAsync(element, eventType, timeoutMs);

            var detailsJson = eventDetails == null ? "null" : $"\"{EscapeJson(eventDetails)}\"";
            return JsonDocument.Parse(
                $"{{\"success\": true, \"fired\": {(fired ? "true" : "false")}, \"eventDetails\": {detailsJson}, \"elapsedMs\": {elapsedMs}}}").RootElement;
        }
        catch (Exception ex) {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
    }

    private Task<JsonElement> OpenContextMenu(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var menu = automation.OpenContextMenu(element);

            if (menu == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Context menu did not appear\"}").RootElement);

            var menuId = _session.CacheElement(menu);
            return Task.FromResult(JsonDocument.Parse(
                $"{{\"success\": true, \"menuElementId\": \"{menuId}\", \"message\": \"Context menu opened. Use click_menu_item or find_element with the menuElementId as parent.\"}}").RootElement);
        }
        catch (Exception ex) {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    // Helper methods
    private string? GetStringArg(JsonElement args, string key) {
        if (args.ValueKind == JsonValueKind.Null)
            return null;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.Null
            ? prop.GetString()
            : null;
    }

    private int GetIntArg(JsonElement args, string key, int defaultValue = 0) {
        if (args.ValueKind == JsonValueKind.Null)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : defaultValue;
    }

    private bool GetBoolArg(JsonElement args, string key, bool defaultValue = false) {
        if (args.ValueKind == JsonValueKind.Null)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.True
            ? true
            : args.TryGetProperty(key, out var prop2) && prop2.ValueKind == JsonValueKind.False
                ? false
                : defaultValue;
    }

    private int? GetNullableIntArg(JsonElement args, string key) {
        if (args.ValueKind == JsonValueKind.Null)
            return null;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }

    private string[]? GetStringArrayArg(JsonElement args, string key) {
        if (args.ValueKind == JsonValueKind.Null)
            return null;

        if (!args.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray()) {
            if (item.ValueKind == JsonValueKind.String)
                result.Add(item.GetString()!);
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    private string EscapeJson(string? value) {
        if (value == null)
            return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}