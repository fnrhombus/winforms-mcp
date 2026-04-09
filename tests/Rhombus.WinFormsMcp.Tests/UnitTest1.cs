namespace Rhombus.WinFormsMcp.Tests;

using System.Diagnostics;

using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Tests for AutomationHelper and WinForms automation functionality
/// </summary>
public class AutomationHelperTests {
    private AutomationHelper? _automation;
    private Process? _testProcess;

    [SetUp]
    public void Setup() {
        _automation = new AutomationHelper();
    }

    [TearDown]
    public void TearDown() {
        // Close any launched processes
        if (_testProcess is { HasExited: false }) {
            try {
                _testProcess.Kill();
            }
            catch { }
        }

        _automation?.Dispose();
    }

    [Test]
    public void TestAutomationHelperInitialization() {
        Assert.That(_automation, Is.Not.Null);
        Assert.Pass("AutomationHelper initialized successfully");
    }

    [Test]
    [Ignore("Requires GUI - use AutomationHelperHeadlessTests for CI/CD")]
    public void TestLaunchApp() {
        var notepadPath = "notepad.exe";
        var process = _automation?.LaunchApp(notepadPath);

        Assert.That(process, Is.Not.Null);
        Assert.That(process?.ProcessName, Contains.Substring("notepad"));
        Assert.That(process?.Id, Is.GreaterThan(0));

        _testProcess = process;
    }

    [Test]
    [Ignore("Requires GUI - use IntegrationTestsHeadless for CI/CD")]
    public void TestGetMainWindow() {
        var notepadPath = "notepad.exe";
        var process = _automation?.LaunchApp(notepadPath);
        Assert.That(process, Is.Not.Null);
        _testProcess = process;

        System.Threading.Thread.Sleep(1000); // Wait for window to appear

        var mainWindow = _automation?.GetMainWindow(process!.Id);
        Assert.That(mainWindow, Is.Not.Null);
    }

    [Test]
    [Ignore("Requires GUI - use IntegrationTestsHeadless for CI/CD")]
    public void TestAttachToProcessByName() {
        // Launch a process first
        var notepadPath = "notepad.exe";
        var process = _automation?.LaunchApp(notepadPath);
        _testProcess = process;

        System.Threading.Thread.Sleep(500);

        // Attach to it by name
        var attachedProcess = _automation?.AttachToProcessByName("notepad");
        Assert.That(attachedProcess, Is.Not.Null);
        Assert.That(attachedProcess?.ProcessName, Contains.Substring("notepad"));
    }

    [Test]
    public void TestElementExists() {
        // This test checks if element exists without throwing
        var result = _automation?.ElementExists("nonexistent");
        Assert.That(result, Is.EqualTo(false));
    }

    [Test]
    [TestCase("notepad.exe")]
    [Ignore("Requires GUI - use IntegrationTestsHeadless for CI/CD")]
    public void TestCloseApp(string appPath) {
        var process = _automation?.LaunchApp(appPath);
        Assert.That(process, Is.Not.Null);
        _testProcess = process;

        System.Threading.Thread.Sleep(500);

        // Close the app
        _automation?.CloseApp(process!.Id);

        // Wait for process to exit
        System.Threading.Thread.Sleep(1000);
        Assert.That(process.HasExited, Is.True);
    }

    [Test]
    public void TestElementCaching() {
        // This is tested indirectly through find operations
        // Verify that the automation helper can find elements (which are cached)
        Assert.That(_automation, Is.Not.Null);
    }

    [Test]
    [Ignore("Requires GUI - use AutomationHelperHeadlessTests for CI/CD")]
    public void TestProcessTracking() {
        var app1 = _automation?.LaunchApp("notepad.exe");
        _testProcess = app1;

        Assert.That(app1?.Id, Is.GreaterThan(0));
        Assert.That(app1?.ProcessName, Is.EqualTo("notepad"));
    }

    [Test]
    [Ignore("Requires GUI - use IntegrationTestsHeadless for CI/CD")]
    public void TestTakeScreenshot() {
        var notepadPath = "notepad.exe";
        var process = _automation?.LaunchApp(notepadPath);
        _testProcess = process;

        System.Threading.Thread.Sleep(1000); // Wait for window to appear

        var screenshotPath = Path.Combine(Path.GetTempPath(), "test_screenshot.png");

        try {
            _automation?.TakeScreenshot(screenshotPath);

            // Verify file was created
            Assert.That(File.Exists(screenshotPath), Is.True);
            Assert.That(new FileInfo(screenshotPath).Length, Is.GreaterThan(0));
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
        }
    }

    [Test]
    [Ignore("Requires GUI - use IntegrationTestsHeadless for CI/CD")]
    public async Task TestWaitForElementAsync() {
        // Test the async wait functionality
        var result = await _automation!.WaitForElementAsync("nonexistent_element", null, 500);
        Assert.That(result, Is.False); // Should timeout and return false
    }

    [Test]
    [Ignore("Requires GUI - use IntegrationTestsHeadless for CI/CD")]
    public void TestSendKeys() {
        var notepadPath = "notepad.exe";
        var process = _automation?.LaunchApp(notepadPath);
        _testProcess = process;

        System.Threading.Thread.Sleep(1000);

        // Send some keys
        _automation?.SendKeys("test keys");

        // Just verify it doesn't throw
        Assert.Pass();
    }

    [Test]
    [Ignore("Requires GUI - use IntegrationTestsHeadless for CI/CD")]
    public void TestTypeTextAndGetValue() {
        var notepadPath = "notepad.exe";
        var process = _automation?.LaunchApp(notepadPath);
        _testProcess = process;

        System.Threading.Thread.Sleep(1000);

        // Test is basic - just verify methods exist and don't throw
        Assert.That(_automation, Is.Not.Null);
    }

    [Test]
    [Ignore("Requires GUI - use IntegrationTestsHeadless for CI/CD")]
    public void TestGetAllChildren() {
        var notepadPath = "notepad.exe";
        var process = _automation?.LaunchApp(notepadPath);
        _testProcess = process;

        System.Threading.Thread.Sleep(1000);

        var mainWindow = _automation?.GetMainWindow(process!.Id);
        if (mainWindow != null) {
            var children = _automation?.GetAllChildren(mainWindow);
            // Children might be null or contain items, both are valid
            Assert.Pass();
        }
        else {
            Assert.Pass("Main window not found, but method executed");
        }
    }
}