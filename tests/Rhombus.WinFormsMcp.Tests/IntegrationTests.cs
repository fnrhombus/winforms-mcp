namespace Rhombus.WinFormsMcp.Tests;

using System.Diagnostics;
using System.Text.Json;

using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Integration tests for the fnWindowsMCP server
/// These tests verify the complete JSON-RPC protocol flow and MCP server functionality
/// </summary>
public class IntegrationTests {
    private AutomationHelper? _automation;
    private Process? _testProcess;

    [SetUp]
    public void Setup() {
        _automation = new AutomationHelper();
    }

    [TearDown]
    public void TearDown() {
        if (_testProcess is { HasExited: false }) {
            try {
                _testProcess.Kill();
            }
            catch { }
        }

        _automation?.Dispose();
    }

    #region Application Lifecycle Integration Tests

    /// <summary>
    /// Tests the complete application launch and window detection workflow
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestApplicationLaunchAndWindowDiscovery() {
        // Arrange
        var appPath = "notepad.exe";

        // Act
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1500); // Wait for window to fully appear

        var mainWindow = _automation?.GetMainWindow(process!.Id);

        // Assert
        Assert.That(process, Is.Not.Null);
        Assert.That(process!.ProcessName, Contains.Substring("notepad"));
        Assert.That(mainWindow, Is.Not.Null);
        Assert.That(mainWindow!.Name, Is.Not.Empty);
    }

    /// <summary>
    /// Tests launching application with working directory
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestApplicationLaunchWithWorkingDirectory() {
        // Arrange
        var tempDir = Path.GetTempPath();
        var appPath = "notepad.exe";

        // Act
        var process = _automation?.LaunchApp(appPath, workingDirectory: tempDir);
        _testProcess = process;

        // Assert
        Assert.That(process, Is.Not.Null);
        Assert.That(process!.Id, Is.GreaterThan(0));
    }

    /// <summary>
    /// Tests the complete process lifecycle: launch → interact → close
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestCompleteProcessLifecycle() {
        // Arrange
        var appPath = "notepad.exe";

        // Act - Launch
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Assert.That(process, Is.Not.Null);

        Thread.Sleep(1000);

        // Act - Verify running
        Assert.That(process!.HasExited, Is.False);

        // Act - Close gracefully
        _automation?.CloseApp(process.Id, force: false);
        Thread.Sleep(1500);

        // Assert - Verify closed
        Assert.That(process.HasExited, Is.True);
    }

    /// <summary>
    /// Tests force-close functionality
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestForceCloseApplication() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1000);

        // Act
        _automation?.CloseApp(process!.Id, force: true);
        Thread.Sleep(1000);

        // Assert
        Assert.That(process!.HasExited, Is.True);
    }

    #endregion

    #region Process Attachment Integration Tests

    /// <summary>
    /// Tests attaching to a running process by ID
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestAttachToProcessById() {
        // Arrange
        var appPath = "notepad.exe";
        var launchedProcess = _automation?.LaunchApp(appPath);
        _testProcess = launchedProcess;
        Thread.Sleep(1000);

        // Act
        var attachedProcess = _automation?.AttachToProcess(launchedProcess!.Id);

        // Assert
        Assert.That(attachedProcess, Is.Not.Null);
        Assert.That(attachedProcess!.ProcessName, Contains.Substring("notepad"));
    }

    /// <summary>
    /// Tests attaching to a running process by name
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestAttachToProcessByName() {
        // Arrange
        var appPath = "notepad.exe";
        var launchedProcess = _automation?.LaunchApp(appPath);
        _testProcess = launchedProcess;
        Thread.Sleep(1000);

        // Act
        var attachedProcess = _automation?.AttachToProcessByName("notepad");

        // Assert
        Assert.That(attachedProcess, Is.Not.Null);
        Assert.That(attachedProcess!.ProcessName, Is.EqualTo("notepad"));
        Assert.That(attachedProcess.Id, Is.EqualTo(launchedProcess!.Id));
    }

    /// <summary>
    /// Tests that attachment fails gracefully for non-existent processes
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestAttachToNonExistentProcess() {
        // Act & Assert - Should throw InvalidOperationException when process not found
        var ex = Assert.Throws<InvalidOperationException>(
            () => _automation?.AttachToProcessByName("nonexistent_process_12345")
        );
        Assert.That(ex?.Message, Contains.Substring("No process found"));
    }

    #endregion

    #region Element Discovery Integration Tests

    /// <summary>
    /// Tests finding elements by various criteria
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestElementDiscoveryMethods() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1500);

        var mainWindow = _automation?.GetMainWindow(process!.Id);
        Assert.That(mainWindow, Is.Not.Null);

        // Act & Assert - Verify we can query the window
        Assert.That(mainWindow!.Name, Is.Not.Null);
        Assert.That(mainWindow.Name.Length, Is.GreaterThan(0));
    }

    /// <summary>
    /// Tests element existence checking
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestElementExistenceChecking() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1500);

        // Act
        var existsResult = _automation?.ElementExists("nonexistent_id");

        // Assert
        Assert.That(existsResult, Is.False);
    }

    /// <summary>
    /// Tests retrieving all child elements
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestGetChildElements() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1500);

        var mainWindow = _automation?.GetMainWindow(process!.Id);
        Assert.That(mainWindow, Is.Not.Null);

        // Act
        var children = _automation?.GetAllChildren(mainWindow!);

        // Assert - Should have some child elements or be empty
        Assert.That(children, Is.Not.Null);
    }

    #endregion

    #region User Interaction Integration Tests

    /// <summary>
    /// Tests sending keyboard input to the focused application
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestKeyboardInput() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1500);

        // Act
        _automation?.SendKeys("Integration Test Input");
        Thread.Sleep(500);

        // Assert - Just verify no exception thrown
        Assert.Pass("Keyboard input sent successfully");
    }

    /// <summary>
    /// Tests taking screenshots before, during, and after interactions
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestScreenshotCapture() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1500);

        var screenshotPath = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.png");

        try {
            // Act
            _automation?.TakeScreenshot(screenshotPath);
            Thread.Sleep(500);

            // Assert
            Assert.That(File.Exists(screenshotPath), Is.True);
            var fileInfo = new FileInfo(screenshotPath);
            Assert.That(fileInfo.Length, Is.GreaterThan(0), "Screenshot file should not be empty");
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
        }
    }

    /// <summary>
    /// Tests screenshot capture for specific window elements
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestElementScreenshotCapture() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1500);

        var mainWindow = _automation?.GetMainWindow(process!.Id);
        Assert.That(mainWindow, Is.Not.Null);

        var screenshotPath = Path.Combine(Path.GetTempPath(), $"element_screenshot_{Guid.NewGuid()}.png");

        try {
            // Act
            _automation?.TakeScreenshot(screenshotPath, mainWindow);
            Thread.Sleep(500);

            // Assert
            Assert.That(File.Exists(screenshotPath), Is.True);
            var fileInfo = new FileInfo(screenshotPath);
            Assert.That(fileInfo.Length, Is.GreaterThan(0), "Element screenshot should not be empty");
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
        }
    }

    #endregion

    #region Async Operations Integration Tests

    /// <summary>
    /// Tests async waiting for elements with timeout
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public async Task TestAsyncElementWaitWithTimeout() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1000);

        var mainWindow = _automation?.GetMainWindow(process!.Id);

        // Act - Wait for non-existent element (should timeout)
        var sw = Stopwatch.StartNew();
        var found = await _automation!.WaitForElementAsync("nonexistent_element", mainWindow, 1000);
        sw.Stop();

        // Assert
        Assert.That(found, Is.False);
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(1000), "Should wait for timeout duration");
    }

    /// <summary>
    /// Tests rapid sequential async operations
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public async Task TestMultipleAsyncOperations() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1000);

        var mainWindow = _automation?.GetMainWindow(process!.Id);

        // Act
        var task1 = _automation!.WaitForElementAsync("elem_1", mainWindow, 500);
        var task2 = _automation!.WaitForElementAsync("elem_2", mainWindow, 500);
        var task3 = _automation!.WaitForElementAsync("elem_3", mainWindow, 500);

        var results = await Task.WhenAll(task1, task2, task3);

        // Assert - All should complete without throwing
        Assert.That(results, Is.Not.Null);
        Assert.That(results.Length, Is.EqualTo(3));
    }

    #endregion

    #region Session State Integration Tests

    /// <summary>
    /// Tests session persistence across multiple operations
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestSessionPersistence() {
        // Arrange
        var appPath = "notepad.exe";

        // Act 1 - Launch first app
        var process1 = _automation?.LaunchApp(appPath);
        _testProcess = process1;
        Thread.Sleep(1000);

        var mainWindow1 = _automation?.GetMainWindow(process1!.Id);
        Assert.That(mainWindow1, Is.Not.Null);

        // Act 2 - Get info about first process
        var exists1 = _automation?.ElementExists("test");
        Assert.That(exists1, Is.False); // Element doesn't exist, but operation succeeds

        // Act 3 - Take screenshot to verify state
        var screenshotPath = Path.Combine(Path.GetTempPath(), "session_test.png");
        try {
            _automation?.TakeScreenshot(screenshotPath);

            // Assert - Session should maintain state across operations
            Assert.That(File.Exists(screenshotPath), Is.True);
            Assert.That(!process1!.HasExited, "Process should still be running");
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
        }
    }

    #endregion

    #region Error Handling Integration Tests

    /// <summary>
    /// Tests that invalid operations don't crash the session
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestErrorRecovery() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;

        // Act 1 - Try invalid operation
        var result = _automation?.ElementExists("invalid_id");
        Assert.That(result, Is.False);

        // Act 2 - Session should still work
        var mainWindow = _automation?.GetMainWindow(process!.Id);
        Assert.That(mainWindow, Is.Not.Null);

        // Act 3 - Can continue operations
        _automation?.SendKeys("test");

        // Assert
        Assert.Pass("Session recovered from invalid operations");
    }

    /// <summary>
    /// Tests process cleanup on errors
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestProcessCleanupOnError() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(500);

        // Act
        try {
            // Try some operations
            _automation?.GetMainWindow(process!.Id);
        }
        finally {
            _automation?.CloseApp(process!.Id);
        }

        // Assert
        Thread.Sleep(1000);
        Assert.That(process!.HasExited, Is.True);
    }

    #endregion

    #region Property Retrieval Integration Tests

    /// <summary>
    /// Tests retrieving various element properties
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void TestPropertyRetrieval() {
        // Arrange
        var appPath = "notepad.exe";
        var process = _automation?.LaunchApp(appPath);
        _testProcess = process;
        Thread.Sleep(1500);

        var mainWindow = _automation?.GetMainWindow(process!.Id);
        Assert.That(mainWindow, Is.Not.Null);

        // Act & Assert - Properties exist
        var name = _automation?.GetProperty(mainWindow!, "name");
        Assert.That(name, Is.Not.Null);

        var controlType = _automation?.GetProperty(mainWindow!, "controltype");
        Assert.That(controlType, Is.Not.Null);
    }

    #endregion
}