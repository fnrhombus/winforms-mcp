using System;
using System.Collections.Generic;
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
    private readonly FormRenderer _formRenderer = new();
    private readonly CompiledFormRenderer _compiledFormRenderer = new();

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

            // Validation Tools
            { "take_screenshot", TakeScreenshot },
            { "element_exists", ElementExists },
            { "wait_for_element", WaitForElement },

            // Interaction Tools
            { "drag_drop", DragDrop },
            { "send_keys", SendKeys },

            // Form Preview Tools
            { "render_form", RenderForm },
            { "render_form_compiled", RenderFormCompiled },

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
                name = "take_screenshot",
                description = "Take a screenshot of the application or element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        outputPath = new { type = "string", description = "Path to save the screenshot" },
                        elementPath = new { type = "string", description = "Specific element to screenshot (optional)" }
                    },
                    required = new[] { "outputPath" }
                }
            },
            new
            {
                name = "render_form",
                description = "Render a WinForms .Designer.cs file to a PNG image preview. The designer file must follow standard WinForms designer conventions: partial class with InitializeComponent() method, fully-qualified type names (e.g. System.Windows.Forms.Button), this. prefix on field access, SuspendLayout/ResumeLayout wrapping. Only standard System.Windows.Forms and System.Drawing types are supported. Event wireups and resource references are ignored.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        designerFilePath = new { type = "string", description = "Path to the .Designer.cs file" },
                        outputPath = new { type = "string", description = "Path to save the rendered PNG" }
                    },
                    required = new[] { "designerFilePath", "outputPath" }
                }
            },
            new
            {
                name = "render_form_compiled",
                description = "Render a WinForms form to a PNG image by compiling a temporary project. Accepts a .cs or .Designer.cs file path — if given a .cs file, automatically uses the sibling .Designer.cs. Copies package references from the source project's .csproj, so custom controls and third-party packages work. Returns the image directly as base64. Results are cached until the source changes. Requires a separate .Designer.cs file to exist.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        sourceFilePath = new { type = "string", description = "Path to the .cs or .Designer.cs file" }
                    },
                    required = new[] { "sourceFilePath" }
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

    private Task<JsonElement> TakeScreenshot(JsonElement args)
    {
        try
        {
            var outputPath = GetStringArg(args, "outputPath") ?? throw new ArgumentException("outputPath is required");
            var elementId = GetStringArg(args, "elementId");

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            if (!string.IsNullOrEmpty(elementId))
                element = _session.GetElement(elementId!);

            automation.TakeScreenshot(outputPath, element);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Screenshot saved to {EscapeJson(outputPath)}\"}}").RootElement);
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
            var outputPath = GetStringArg(args, "outputPath") ?? throw new ArgumentException("outputPath is required");

            _formRenderer.RenderDesignerFile(designerFilePath, outputPath);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Form rendered to {EscapeJson(outputPath)}\"}}").RootElement);
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

            var pngBytes = _compiledFormRenderer.RenderForm(sourceFilePath);
            var base64 = Convert.ToBase64String(pngBytes);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"imageBase64\": \"{base64}\"}}").RootElement);
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

    private string EscapeJson(string? value)
    {
        if (value == null)
            return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
