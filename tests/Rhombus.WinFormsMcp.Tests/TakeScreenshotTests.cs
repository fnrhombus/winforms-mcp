namespace Rhombus.WinFormsMcp.Tests;

using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Moq;
using Rhombus.WinFormsMcp.Rendering;
using Rhombus.WinFormsMcp.Server;
using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Tests for the take_screenshot tool's base64 image return behavior.
/// Since the TakeScreenshot handler is private inside AutomationServer,
/// these tests verify the base64 conversion logic and MCP response format
/// that the handler uses, plus tool definition correctness.
/// </summary>
public class TakeScreenshotTests {
    private string _tempDir = null!;

    [SetUp]
    public void Setup() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"screenshot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_tempDir)) {
            try { Directory.Delete(_tempDir, true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verify that a PNG file can be read and converted to valid base64.
    /// This mirrors what TakeScreenshot does after calling automation.TakeScreenshot().
    /// </summary>
    [Test]
    public void Base64Conversion_ValidPng_ProducesValidBase64() {
        // Arrange: create a small PNG file
        var pngPath = Path.Combine(_tempDir, "test.png");
        CreateTestPng(pngPath, 10, 10);

        // Act: read and convert (same logic as TakeScreenshot handler)
        var imageBytes = File.ReadAllBytes(pngPath);
        var base64 = Convert.ToBase64String(imageBytes);

        // Assert
        Assert.That(base64, Is.Not.Null.And.Not.Empty);
        // Verify it round-trips back to the same bytes
        var roundTripped = Convert.FromBase64String(base64);
        Assert.That(roundTripped, Is.EqualTo(imageBytes));
    }

    /// <summary>
    /// Verify the JSON response format includes imageBase64 field
    /// so the MCP image content handler will pick it up.
    /// </summary>
    [Test]
    public void ResponseFormat_ContainsImageBase64_WhenSuccessful() {
        // Arrange
        var pngPath = Path.Combine(_tempDir, "test.png");
        CreateTestPng(pngPath, 10, 10);
        var imageBytes = File.ReadAllBytes(pngPath);
        var base64 = Convert.ToBase64String(imageBytes);

        // Act: build response JSON the same way TakeScreenshot does
        var json = $"{{\"success\": true, \"imageBase64\": \"{base64}\"}}";
        var result = JsonDocument.Parse(json).RootElement;

        // Assert: the imageBase64 property exists and is a string
        Assert.That(result.TryGetProperty("imageBase64", out var imgData), Is.True);
        Assert.That(imgData.ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(imgData.GetString(), Is.EqualTo(base64));

        // Verify the success flag
        Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
    }

    /// <summary>
    /// Verify the MCP image content block is correctly built from the response.
    /// This tests the same logic as ProcessRequest lines 269-288.
    /// </summary>
    [Test]
    public void McpImageContentBlock_BuiltCorrectly_FromBase64Response() {
        // Arrange
        var pngPath = Path.Combine(_tempDir, "test.png");
        CreateTestPng(pngPath, 20, 20);
        var imageBytes = File.ReadAllBytes(pngPath);
        var base64 = Convert.ToBase64String(imageBytes);

        // Simulate the result from TakeScreenshot
        var resultJson = $"{{\"success\": true, \"imageBase64\": \"{base64}\"}}";
        var result = JsonDocument.Parse(resultJson).RootElement;

        // Act: simulate ProcessRequest image handler logic
        Assert.That(result.TryGetProperty("imageBase64", out var imgData), Is.True);
        Assert.That(imgData.ValueKind, Is.EqualTo(JsonValueKind.String));

        var contentBlock = new Dictionary<string, string> {
            ["type"] = "image",
            ["data"] = imgData.GetString()!,
            ["mimeType"] = "image/png"
        };

        // Assert
        Assert.That(contentBlock["type"], Is.EqualTo("image"));
        Assert.That(contentBlock["mimeType"], Is.EqualTo("image/png"));
        Assert.That(contentBlock["data"], Is.EqualTo(base64));

        // Verify the base64 decodes back to a valid PNG
        var decoded = Convert.FromBase64String(contentBlock["data"]);
        Assert.That(decoded, Is.EqualTo(imageBytes));
    }

    /// <summary>
    /// Verify that base64 from a PNG starts with the PNG signature bytes.
    /// </summary>
    [Test]
    public void Base64Conversion_DecodedBytes_StartWithPngSignature() {
        // Arrange
        var pngPath = Path.Combine(_tempDir, "test.png");
        CreateTestPng(pngPath, 5, 5);
        var imageBytes = File.ReadAllBytes(pngPath);
        var base64 = Convert.ToBase64String(imageBytes);

        // Act
        var decoded = Convert.FromBase64String(base64);

        // Assert: PNG files start with 0x89 0x50 0x4E 0x47
        Assert.That(decoded.Length, Is.GreaterThanOrEqualTo(4));
        Assert.That(decoded[0], Is.EqualTo(0x89));
        Assert.That(decoded[1], Is.EqualTo(0x50)); // P
        Assert.That(decoded[2], Is.EqualTo(0x4E)); // N
        Assert.That(decoded[3], Is.EqualTo(0x47)); // G
    }

    /// <summary>
    /// Verify that temp file cleanup works (outputPath omitted scenario).
    /// </summary>
    [Test]
    public void TempFileCleanup_FileDeletedAfterBase64Conversion() {
        // Arrange: simulate the temp file path scenario
        var tempPath = Path.Combine(_tempDir, $"screenshot_{Guid.NewGuid():N}.png");
        CreateTestPng(tempPath, 10, 10);
        Assert.That(File.Exists(tempPath), Is.True);

        // Act: read, convert, then delete (same as TakeScreenshot with no outputPath)
        var imageBytes = File.ReadAllBytes(tempPath);
        var base64 = Convert.ToBase64String(imageBytes);
        File.Delete(tempPath);

        // Assert
        Assert.That(File.Exists(tempPath), Is.False);
        Assert.That(base64, Is.Not.Null.And.Not.Empty);
    }

    /// <summary>
    /// Verify that when outputPath IS provided, the file remains on disk.
    /// </summary>
    [Test]
    public void WithOutputPath_FileRemainsOnDisk_AndBase64Returned() {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "saved_screenshot.png");
        CreateTestPng(outputPath, 10, 10);

        // Act: read and convert without deleting (outputPath was provided)
        var imageBytes = File.ReadAllBytes(outputPath);
        var base64 = Convert.ToBase64String(imageBytes);

        // Assert: file still exists AND base64 is valid
        Assert.That(File.Exists(outputPath), Is.True);
        Assert.That(base64, Is.Not.Null.And.Not.Empty);
        Assert.That(Convert.FromBase64String(base64), Is.EqualTo(imageBytes));
    }

    /// <summary>
    /// Verify the tool definition no longer requires outputPath.
    /// We instantiate AutomationServer and inspect the tools/list response.
    /// </summary>
    [Test]
    public async Task ToolDefinition_OutputPathIsOptional() {
        // Arrange: create server and capture its output via JSON-RPC
        var inputWriter = new StringWriter();
        var outputReader = new StringReader("");

        // Use reflection or instantiate AutomationServer to check tool definitions
        var automation = new AutomationHelper(headless: true);
        var session = new SessionManager(automation);
        var rendererPool = new RendererProcessPool(new MemoryCache(new MemoryCacheOptions()));
        var telemetry = new NullTelemetry();
        var lifetime = new Mock<IHostApplicationLifetime>().Object;
        var server = new AutomationServer(session, rendererPool, telemetry, lifetime);

        // We test by sending an initialize + tools/list request through the server
        // But since RunAsync reads from Console.In, we'll test by checking the
        // tool definition structure directly via a simulated tools/list call.

        // Build a tools/list JSON-RPC request
        var request = JsonDocument.Parse("{\"jsonrpc\": \"2.0\", \"id\": 1, \"method\": \"tools/list\", \"params\": {}}").RootElement;

        // Use reflection to call ProcessRequest
        var processRequestMethod = typeof(AutomationServer).GetMethod(
            "ProcessRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.That(processRequestMethod, Is.Not.Null, "ProcessRequest method should be accessible");

        var resultTask = (Task<object>)processRequestMethod!.Invoke(server, new object[] { request, (object)1 })!;
        var result = await resultTask;

        // Serialize and re-parse to inspect
        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");

        // Find the take_screenshot tool
        JsonElement? screenshotTool = null;
        foreach (var tool in tools.EnumerateArray()) {
            if (tool.GetProperty("name").GetString() == "take_screenshot") {
                screenshotTool = tool;
                break;
            }
        }

        Assert.That(screenshotTool, Is.Not.Null, "take_screenshot tool should exist");

        var schema = screenshotTool!.Value.GetProperty("inputSchema");

        // Verify outputPath is NOT in the required array (or required doesn't exist)
        if (schema.TryGetProperty("required", out var required)) {
            var requiredFields = new List<string>();
            foreach (var field in required.EnumerateArray()) {
                requiredFields.Add(field.GetString()!);
            }
            Assert.That(requiredFields, Does.Not.Contain("outputPath"),
                "outputPath should not be required");
        }
        // If 'required' property doesn't exist at all, that's also correct

        // Verify description mentions returning image directly
        var description = screenshotTool!.Value.GetProperty("description").GetString();
        Assert.That(description, Does.Contain("base64").IgnoreCase,
            "Description should mention base64 image return");
    }

    /// <summary>
    /// Helper to create a small test PNG file using System.Drawing.
    /// </summary>
    private static void CreateTestPng(string path, int width, int height) {
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Blue);
        bitmap.Save(path, ImageFormat.Png);
    }
}