using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Rhombus.WinFormsMcp.Tests;

/// <summary>
/// Integration tests that spawn the MCP server as a child process
/// and verify it speaks the MCP protocol correctly over stdio.
/// </summary>
[TestFixture]
public class McpIntegrationTests {
    private Process? _serverProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;

    [SetUp]
    public void Setup() {
        var serverExe = Path.Combine(TestContext.CurrentContext.TestDirectory, "winformsmcp.exe");
        Assert.That(File.Exists(serverExe),
            $"Server exe not found at {serverExe}");

        _serverProcess = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = serverExe,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment = { ["HEADLESS"] = "true" }
            }
        };
        _serverProcess.Start();
        _stdin = _serverProcess.StandardInput;
        _stdout = _serverProcess.StandardOutput;
    }

    [TearDown]
    public void TearDown() {
        if (_serverProcess != null && !_serverProcess.HasExited) {
            _stdin?.Close(); // closing stdin causes the server to exit gracefully
            _serverProcess.WaitForExit(5000);
            if (!_serverProcess.HasExited) {
                _serverProcess.Kill();
            }
        }
        _serverProcess?.Dispose();
    }

    [Test]
    [Timeout(30000)]
    public async Task Initialize_ReturnsValidMcpResponse() {
        var response = await SendRequest("initialize", new {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });

        Assert.That(response.TryGetProperty("result", out var result), Is.True,
            "Response should have 'result' property");
        Assert.That(result.TryGetProperty("protocolVersion", out _), Is.True,
            "Result should have 'protocolVersion'");
        Assert.That(result.TryGetProperty("capabilities", out var caps), Is.True,
            "Result should have 'capabilities'");
        Assert.That(caps.TryGetProperty("tools", out _), Is.True,
            "Capabilities should have 'tools'");
        Assert.That(result.TryGetProperty("serverInfo", out var info), Is.True,
            "Result should have 'serverInfo'");
        Assert.That(info.GetProperty("name").GetString(), Is.Not.Empty,
            "Server name should not be empty");
    }

    [Test]
    [Timeout(30000)]
    public async Task ToolsList_ReturnsAllExpectedTools() {
        // Must initialize first per MCP protocol
        await SendRequest("initialize", new {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });

        // Send initialized notification (no id = notification)
        await SendNotification("notifications/initialized");

        // Now request tools list
        var response = await SendRequest("tools/list", new { });

        Assert.That(response.TryGetProperty("result", out var result), Is.True,
            "Response should have 'result' property");
        Assert.That(result.TryGetProperty("tools", out var tools), Is.True,
            "Result should have 'tools' array");
        Assert.That(tools.ValueKind, Is.EqualTo(JsonValueKind.Array),
            "Tools should be an array");

        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!)
            .ToHashSet();

        // Verify all tools that have definitions in GetToolDefinitions() are present.
        var expectedTools = new[] {
            "winforms_find_element",
            "winforms_click_element",
            "winforms_type_text",
            "winforms_get_property",
            "winforms_launch_app",
            "winforms_get_process_status",
            "winforms_take_screenshot",
            "winforms_render_form",
            "winforms_select_item",
            "winforms_click_menu_item",
            "winforms_get_element_tree",
            "winforms_set_value",
            "winforms_attach_to_process",
            "winforms_close_app",
            "winforms_element_exists",
            "winforms_wait_for_element",
            "winforms_drag_drop",
            "winforms_send_keys",
            "winforms_wait_for_condition",
            "winforms_toggle_element",
            "winforms_scroll_element",
            "winforms_get_table_data",
            "winforms_set_table_cell",
            "winforms_manage_window",
            "winforms_list_windows",
            "winforms_get_focused_element",
            "winforms_raise_event",
            "winforms_listen_for_event",
            "winforms_open_context_menu",
            "winforms_get_clipboard",
            "winforms_set_clipboard",
            "winforms_read_tooltip",
            "winforms_find_elements",
        };

        var expectedSet = new HashSet<string>(expectedTools);
        var missing = new HashSet<string>(expectedSet);
        missing.ExceptWith(toolNames);
        var extra = new HashSet<string>(toolNames);
        extra.ExceptWith(expectedSet);

        Assert.Multiple(() => {
            Assert.That(missing, Is.Empty, $"Missing tools: {string.Join(", ", missing)}");
            Assert.That(extra, Is.Empty, $"Unexpected tools: {string.Join(", ", extra)}");
        });

        TestContext.WriteLine($"Server exposes {toolNames.Count} tools:");
        foreach (var name in toolNames.OrderBy(n => n)) {
            TestContext.WriteLine($"  - {name}");
        }
    }

    [Test]
    [Timeout(30000)]
    public async Task ToolsList_AllToolsHaveDescriptionAndInputSchema() {
        await SendRequest("initialize", new {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });
        await SendNotification("notifications/initialized");

        var response = await SendRequest("tools/list", new { });
        var tools = response.GetProperty("result").GetProperty("tools");

        foreach (var tool in tools.EnumerateArray()) {
            var name = tool.GetProperty("name").GetString();

            Assert.That(tool.TryGetProperty("description", out var desc), Is.True,
                $"Tool '{name}' missing 'description'");
            Assert.That(desc.GetString(), Is.Not.Empty,
                $"Tool '{name}' has empty description");

            Assert.That(tool.TryGetProperty("inputSchema", out var schema), Is.True,
                $"Tool '{name}' missing 'inputSchema'");
            Assert.That(schema.TryGetProperty("type", out _), Is.True,
                $"Tool '{name}' inputSchema missing 'type'");
        }
    }

    [Test]
    [Timeout(30000)]
    public async Task ToolsCall_UnknownTool_ReturnsError() {
        await SendRequest("initialize", new {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });
        await SendNotification("notifications/initialized");

        var response = await SendRequest("tools/call", new {
            name = "nonexistent_tool",
            arguments = new { }
        });

        Assert.That(response.TryGetProperty("error", out var error), Is.True,
            "Should return error for unknown tool");
        // The actual error detail is in error.data.details
        var details = error.GetProperty("data").GetProperty("details").GetString();
        Assert.That(details, Does.Contain("Unknown tool"));
    }

    [Test]
    [Timeout(30000)]
    public async Task ToolsCall_RenderForm_WithValidDesignerCode_ReturnsImage() {
        await SendRequest("initialize", new {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });
        await SendNotification("notifications/initialized");

        // Write a temp designer file with a csproj (needed for TFM auto-detection)
        var tempDir = Path.Combine(Path.GetTempPath(), $"mcp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Test.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0-windows</TargetFramework></PropertyGroup></Project>");
        var designerPath = Path.Combine(tempDir, "TestForm.Designer.cs");
        File.WriteAllText(designerPath, @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.Text = ""MCP Integration Test"";
            this.ClientSize = new System.Drawing.Size(300, 200);
        }
    }
}");
        try {
            var response = await SendRequest("tools/call", new {
                name = "winforms_render_form",
                arguments = new { designerFilePath = designerPath }
            });

            // render_form returns image content block
            Assert.That(response.TryGetProperty("result", out var result), Is.True,
                "Should have result");
            Assert.That(result.TryGetProperty("content", out var content), Is.True,
                "Result should have content array (MCP image block)");

            var firstBlock = content[0];
            var blockType = firstBlock.GetProperty("type").GetString();
            if (blockType == "text") {
                Assert.Ignore("render_form returned text instead of image — GUI/DesignSurface not available (headless CI)");
            }
            Assert.That(blockType, Is.EqualTo("image"),
                "Content block type should be 'image'");
            Assert.That(firstBlock.GetProperty("mimeType").GetString(), Is.EqualTo("image/png"),
                "MIME type should be image/png");

            var base64 = firstBlock.GetProperty("data").GetString();
            Assert.That(base64, Is.Not.Null.And.Not.Empty,
                "Image data should not be empty");

            // Verify it's valid base64 that decodes to a PNG
            var pngBytes = Convert.FromBase64String(base64!);
            Assert.That(pngBytes[0], Is.EqualTo(0x89), "PNG magic byte 0");
            Assert.That(pngBytes[1], Is.EqualTo(0x50), "PNG magic byte 1 ('P')");
            Assert.That(pngBytes[2], Is.EqualTo(0x4E), "PNG magic byte 2 ('N')");
            Assert.That(pngBytes[3], Is.EqualTo(0x47), "PNG magic byte 3 ('G')");

            TestContext.WriteLine($"render_form returned valid PNG ({pngBytes.Length:N0} bytes)");
        }
        finally {
            Directory.Delete(tempDir, true);
        }
    }

    #region Claude CLI Integration

    [Test]
    [Timeout(60000)]
    [Category("RequiresClaude")]
    public async Task ClaudeMcpGet_ShowsServerWithAllTools() {
        // Check claude CLI is available
        var claudePath = FindClaude();
        if (claudePath == null) {
            Assert.Ignore("Claude CLI not found on PATH — skipping claude mcp integration test");
        }

        // Create a temp directory with .mcp.json pointing to our server
        var tempDir = Path.Combine(Path.GetTempPath(), $"mcp_claude_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var mcpConfig = new {
            mcpServers = new Dictionary<string, object> {
                ["winforms-mcp"] = new {
                    command = Path.Combine(TestContext.CurrentContext.TestDirectory, "winformsmcp.exe"),
                    args = Array.Empty<string>(),
                    env = new Dictionary<string, string> { ["HEADLESS"] = "true" }
                }
            }
        };
        File.WriteAllText(
            Path.Combine(tempDir, ".mcp.json"),
            JsonSerializer.Serialize(mcpConfig, new JsonSerializerOptions { WriteIndented = true }));

        try {
            var psi = new ProcessStartInfo {
                FileName = claudePath,
                Arguments = "mcp get winforms-mcp",
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            TestContext.WriteLine($"Exit code: {process.ExitCode}");
            TestContext.WriteLine($"stdout:\n{stdout}");
            if (!string.IsNullOrEmpty(stderr)) {
                TestContext.WriteLine($"stderr:\n{stderr}");
            }

            Assert.That(process.ExitCode, Is.EqualTo(0),
                $"claude mcp get should succeed. stderr: {stderr}");

            // claude mcp get shows server name, scope, and connection status
            Assert.That(stdout, Does.Contain("winforms-mcp"),
                "Server name should appear in output");
            Assert.That(stdout, Does.Contain("Connected"),
                "Server should show as connected — proves MCP handshake succeeded");
        }
        finally {
            Directory.Delete(tempDir, true);
        }
    }

    private static string? FindClaude() {
        try {
            var psi = new ProcessStartInfo {
                FileName = "claude",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0 ? "claude" : null;
        }
        catch {
            return null;
        }
    }

    #endregion

    #region Helpers

    private int _nextRequestId = 1;

    private async Task<JsonElement> SendRequest(string method, object @params) {
        var request = JsonSerializer.Serialize(new {
            jsonrpc = "2.0",
            id = _nextRequestId++,
            method,
            @params
        });

        await _stdin!.WriteLineAsync(request);
        await _stdin.FlushAsync();

        var responseLine = await _stdout!.ReadLineAsync()
            ?? throw new InvalidOperationException("Server closed stdout unexpectedly");

        return JsonDocument.Parse(responseLine).RootElement;
    }

    private async Task SendNotification(string method) {
        var notification = JsonSerializer.Serialize(new {
            jsonrpc = "2.0",
            method
        });

        await _stdin!.WriteLineAsync(notification);
        await _stdin.FlushAsync();

        // Notifications don't get a response — small delay to let server process it
        await Task.Delay(100);
    }

    #endregion
}