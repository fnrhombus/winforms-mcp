using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FlaUI.Core.AutomationElements;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Rhombus.WinFormsMcp.Rendering;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server;

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
            { "winforms_find_element", FindElement },
            { "winforms_click_element", ClickElement },
            { "winforms_type_text", TypeText },
            { "winforms_set_value", SetValue },
            { "winforms_get_property", GetProperty },

            // Process Tools
            { "winforms_launch_app", LaunchApp },
            { "winforms_attach_to_process", AttachToProcess },
            { "winforms_close_app", CloseApp },
            { "winforms_get_process_status", GetProcessStatus },

            // Validation Tools
            { "winforms_take_screenshot", TakeScreenshot },
            { "winforms_element_exists", ElementExists },
            { "winforms_wait_for_element", WaitForElement },

            // Interaction Tools
            { "winforms_drag_drop", DragDrop },
            { "winforms_send_keys", SendKeys },
            { "winforms_select_item", SelectItem },
            { "winforms_click_menu_item", ClickMenuItem },

            // Form Preview Tools
            { "winforms_render_form", RenderForm },

            // Discovery Tools
            { "winforms_get_element_tree", GetElementTree },

            // Condition & Toggle Tools
            { "winforms_wait_for_condition", WaitForCondition },
            { "winforms_toggle_element", ToggleElement },

            // Data & Scroll Tools
            { "winforms_scroll_element", ScrollElement },
            { "winforms_get_table_data", GetTableData },
            { "winforms_set_table_cell", SetTableCell },

            // Window Management Tools
            { "winforms_manage_window", ManageWindow },
            { "winforms_list_windows", ListWindows },
            { "winforms_get_focused_element", GetFocusedElement },

            // Event Tools
            { "winforms_raise_event", RaiseEvent },
            { "winforms_listen_for_event", ListenForEvent },
            { "winforms_open_context_menu", OpenContextMenu },

            // Polish Tools
            { "winforms_get_clipboard", GetClipboard },
            { "winforms_set_clipboard", SetClipboard },
            { "winforms_read_tooltip", ReadTooltip },
            { "winforms_find_elements", FindElements },
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

    private Task ProcessNotification(JsonElement _) {
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
                name = "winforms_find_element",
                description = "Find a UI element by AutomationId, Name, ClassName, or ControlType. Returns a cached element ID for use with other tools.",
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
                name = "winforms_click_element",
                description = "Click on a cached UI element. Supports single and double click.",
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
                name = "winforms_type_text",
                description = "Type text into a text field using keyboard simulation. Use winforms_set_value for hidden desktops.",
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
                name = "winforms_get_property",
                description = "Get a property or UIA pattern value from a cached UI element. " +
                    "Returns the property value, or an error listing all supported property names if the requested property is unknown.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID (e.g. elem_1)" },
                        propertyName = new { type = "string", description = "Property name to read" }
                    },
                    required = new[] { "elementId", "propertyName" }
                }
            },
            new
            {
                name = "winforms_launch_app",
                description = "Launch a WinForms application. Returns the process ID for use with other tools.",
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
                name = "winforms_get_process_status",
                description = "Get the current status of a launched process including exit code, responsiveness, and stderr output.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID returned by winforms_launch_app or winforms_attach_to_process" }
                    },
                    required = new[] { "pid" }
                }
            },
            new
            {
                name = "winforms_take_screenshot",
                description = "Take a screenshot of the application or element as base64 PNG. Works on both visible and headless desktops via PrintWindow.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID — captures the process's main window via PrintWindow (recommended)" },
                        outputPath = new { type = "string", description = "Path to save the screenshot (optional). If omitted, returns base64 only." },
                        elementPath = new { type = "string", description = "Specific element to screenshot (optional)" }
                    }
                }
            },
            // ── Form Preview Tools ──
            new
            {
                name = "winforms_render_form",
                description = "Render a WinForms .Designer.cs file to a pixel-accurate PNG preview without building the project. " +
                    "Auto-detects target framework and returns base64 PNG. Results are cached by content hash.",
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
                name = "winforms_select_item",
                description = "Select an item in a combo box, list box, or similar selection control by text or index. " +
                    "Handles expand/collapse automatically.",
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
                name = "winforms_click_menu_item",
                description = "Navigate and click a menu item by path (e.g. [\"File\", \"Save As\"]). " +
                    "Works with menu bars and context menus.",
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
                name = "winforms_get_element_tree",
                description = "Get the UI element tree as structured JSON. Each element is cached with an elementId for subsequent tool calls. " +
                    "Use this to discover UI structure before interacting with elements.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID — uses the process main window as root" },
                        elementId = new { type = "string", description = "Cached element ID to use as root (from a previous find call)" },
                        depth = new { type = "integer", description = "Maximum depth to traverse (default 3)" },
                        maxElements = new { type = "integer", description = "Maximum total elements to include (default 50)" }
                    }
                }
            },
            // ── Value & Process Tools ──
            new
            {
                name = "winforms_set_value",
                description = "Set a value on a UI element via UIA ValuePattern. Works on hidden desktops (no keystroke simulation).",
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
                name = "winforms_attach_to_process",
                description = "Attach to an already-running process by PID or name. Always uses the default (visible) desktop.",
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
                name = "winforms_close_app",
                description = "Close a running application gracefully (WM_CLOSE) or forcefully (Process.Kill).",
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
                name = "winforms_element_exists",
                description = "Quick boolean check for whether a UI element exists by AutomationId (1-second timeout).",
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
                name = "winforms_wait_for_element",
                description = "Wait for a UI element to appear by AutomationId, polling at 100ms intervals.",
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
                name = "winforms_drag_drop",
                description = "Drag-and-drop from one element to another. Uses mouse simulation (visible desktop only).",
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
                name = "winforms_send_keys",
                description = "Send keyboard input using SendKeys syntax (^ = Ctrl, % = Alt, + = Shift). Visible desktop only.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        keys = new { type = "string", description = "Keys to send using SendKeys syntax (e.g. \"^a\" for Ctrl+A, \"%{F4}\" for Alt+F4)" },
                        pid = new { type = "integer", description = "Optional process ID to focus before sending keys" }
                    },
                    required = new[] { "keys" }
                }
            },
            // ── Condition & Toggle Tools ──
            new
            {
                name = "winforms_wait_for_condition",
                description = "Wait for a UI element's property to match a value. Polls at 100ms intervals. " +
                    "Property names are the same as winforms_get_property.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID to poll" },
                        propertyName = new { type = "string", description = "Property name to check (same names as winforms_get_property)" },
                        expectedValue = new { type = "string", description = "The value to wait for" },
                        comparison = new { type = "string", description = "Comparison type: equals (default), contains, not_equals, greater_than, less_than" },
                        timeoutMs = new { type = "integer", description = "Maximum wait time in milliseconds (default 10000)" }
                    },
                    required = new[] { "elementId", "propertyName", "expectedValue" }
                }
            },
            new
            {
                name = "winforms_toggle_element",
                description = "Toggle a checkbox, radio button, or toggle button via UIA TogglePattern. Works on hidden desktops.",
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
            // ── Data & Scroll Tools ──
            new
            {
                name = "winforms_scroll_element",
                description = "Scroll within a scrollable control using UIA ScrollPattern. Works on hidden desktops.",
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
                name = "winforms_get_table_data",
                description = "Read data from a DataGridView or grid control. Supports pagination via startRow/rowCount.",
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
                name = "winforms_set_table_cell",
                description = "Set a cell value in a DataGridView or grid control. Returns the previous and new values.",
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
            // ── Window Management Tools ──
            new
            {
                name = "winforms_manage_window",
                description = "Manage a window: maximize, minimize, restore, resize, move, or close via UIA patterns.",
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
                name = "winforms_list_windows",
                description = "List all top-level windows for a process. Each window is cached with an elementId.",
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
            // ── Event Tools ──
            new
            {
                name = "winforms_raise_event",
                description = "Raise a UIA pattern event on an element (invoke, toggle, expand, collapse, select, scroll_into_view).",
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
                name = "winforms_listen_for_event",
                description = "Listen for a UIA event (focus_changed, structure_changed, property_changed) with a timeout.",
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
                name = "winforms_open_context_menu",
                description = "Open a context menu on an element. Works on both visible and hidden desktops.",
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
                name = "winforms_get_focused_element",
                description = "Get the UI element that currently has keyboard focus. Returns cached elementId for subsequent interactions.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Optional process ID to verify the focused element belongs to this process" }
                    }
                }
            },
            // ── Clipboard & Misc Tools ──
            new
            {
                name = "winforms_get_clipboard",
                description = "Get the current text content of the Windows clipboard.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "winforms_set_clipboard",
                description = "Set the Windows clipboard text content.",
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
                name = "winforms_read_tooltip",
                description = "Read tooltip or help text from a UI element.",
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
                name = "winforms_find_elements",
                description = "Find all UI elements matching a search criterion. Returns an array of cached element IDs.",
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

    private static readonly string[] SupportedPropertyNames = {
        "name", "automationId", "className", "controlType", "isOffscreen", "isEnabled",
        "value", "text", "isChecked", "toggleState", "isSelected", "selectedItem",
        "items", "itemCount", "boundingRectangle", "isExpanded", "min", "max", "current"
    };

    private static readonly string SupportedPropertyList =
        "Supported property names: " +
        "name, automationId, className, controlType, isOffscreen, isEnabled, " +
        "value (or text) - reads ValuePattern.Value (falls back to Name), " +
        "isChecked (or toggleState) - reads TogglePattern.ToggleState, " +
        "isSelected - reads SelectionItemPattern.IsSelected, " +
        "selectedItem - reads first SelectionPattern.Selection item name, " +
        "items - JSON array of child item names, " +
        "itemCount - count of child items, " +
        "boundingRectangle - JSON {x, y, width, height}, " +
        "isExpanded - reads ExpandCollapsePattern state, " +
        "min / max / current - reads RangeValuePattern values (for sliders, NumericUpDown, etc.).";

    private Task<JsonElement> GetProperty(JsonElement args) {
        try {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var propertyName = GetStringArg(args, "propertyName") ?? "";

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            // Check if the property name is recognized before querying
            if (!SupportedPropertyNames.Contains(propertyName.ToLower())) {
                var json = $"{{\"success\": false, \"error\": \"Unknown property '{EscapeJson(propertyName)}'. {EscapeJson(SupportedPropertyList)}\"}}";
                return Task.FromResult(JsonDocument.Parse(json).RootElement);
            }

            var automation = _session.GetAutomation();
            var value = automation.GetProperty(element, propertyName);

            var valueJson = value == null ? "null" : $"\"{EscapeJson(value.ToString())}\"";
            var resultJson = $"{{\"success\": true, \"propertyName\": \"{propertyName}\", \"value\": {valueJson}}}";
            return Task.FromResult(JsonDocument.Parse(resultJson).RootElement);
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

    private const string RenderFormAuthoringHint =
        "AUTHORING REQUIREMENTS: Forms must use the standard Visual Studio designer file convention - " +
        "a separate .Designer.cs file containing a partial class with InitializeComponent(), " +
        "fully-qualified type names (e.g., System.Windows.Forms.Button), 'this.' prefix for member access, " +
        "SuspendLayout()/ResumeLayout(false)/PerformLayout() calls, field declarations at the bottom, and a Dispose method with components container. " +
        "Do NOT put control creation in the .cs file - it belongs ONLY in .Designer.cs.";

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
            return JsonDocument.Parse($"{{\"success\": true, \"imageBase64\": \"{base64}\", \"hint\": \"{EscapeJson(RenderFormAuthoringHint)}\"}}").RootElement;
        }
        catch (Exception ex) {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\", \"hint\": \"{EscapeJson(RenderFormAuthoringHint)}\"}}").RootElement;
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