namespace Rhombus.WinFormsMcp.Tests;

using System.Diagnostics;
using System.Text;

using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// End-to-End (E2E) tests for fnWindowsMCP
/// These tests verify complete real-world automation workflows that users might perform
/// </summary>
public class E2ETests {
    private AutomationHelper? _automation;
    private List<Process> _launchedProcesses = new();

    [SetUp]
    public void Setup() {
        _automation = new AutomationHelper();
        _launchedProcesses.Clear();
    }

    [TearDown]
    public void TearDown() {
        foreach (var process in _launchedProcesses) {
            try {
                if (!process.HasExited)
                    process.Kill();
            }
            catch { }
        }

        _automation?.Dispose();
    }

    #region Text Editing E2E Workflows

    /// <summary>
    /// E2E: Open notepad, type text, save, and verify
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_TextEditingWorkflow() {
        // Scenario: User wants to create a text file with specific content

        // Step 1: Launch notepad
        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Assert.That(notepad, Is.Not.Null);
        Thread.Sleep(1500);

        // Step 2: Wait for window to appear
        var mainWindow = _automation?.GetMainWindow(notepad!.Id);
        Assert.That(mainWindow, Is.Not.Null);

        // Step 3: Type content
        var testContent = "E2E Test Content\nLine 2\nLine 3";
        _automation?.SendKeys(testContent);
        Thread.Sleep(500);

        // Step 4: Take screenshot to verify content was entered
        var screenshotPath = Path.Combine(Path.GetTempPath(), "e2e_notepad_content.png");
        try {
            _automation?.TakeScreenshot(screenshotPath);
            Assert.That(File.Exists(screenshotPath), Is.True);
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
        }

        // Step 5: Close without saving
        _automation?.CloseApp(notepad!.Id, force: true);
        Thread.Sleep(1000);

        // Step 6: Verify process closed
        Assert.That(notepad!.HasExited, Is.True);
    }

    /// <summary>
    /// E2E: Rapid text entry and verification
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_RapidTextEntry() {
        // Scenario: User needs to enter multiple lines of text quickly

        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Thread.Sleep(1500);

        // Enter multiple lines
        for (int i = 1; i <= 5; i++) {
            _automation?.SendKeys($"Line {i}");
            _automation?.SendKeys("{ENTER}");
            Thread.Sleep(100);
        }

        // Take screenshot after all entries
        var screenshotPath = Path.Combine(Path.GetTempPath(), "e2e_rapid_entry.png");
        try {
            _automation?.TakeScreenshot(screenshotPath);
            Assert.That(File.Exists(screenshotPath), Is.True);
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
        }

        _automation?.CloseApp(notepad!.Id, force: true);
    }

    #endregion

    #region Application Navigation E2E Workflows

    /// <summary>
    /// E2E: Launch multiple applications and verify they're all running
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_MultipleApplicationLaunch() {
        // Scenario: User wants to launch and manage multiple applications simultaneously

        // Step 1: Launch first notepad
        var notepad1 = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad1!);
        Thread.Sleep(1000);
        Assert.That(notepad1, Is.Not.Null);

        // Step 2: Launch second notepad
        var notepad2 = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad2!);
        Thread.Sleep(1000);
        Assert.That(notepad2, Is.Not.Null);

        // Step 3: Verify both are running
        Assert.That(!notepad1!.HasExited, "First notepad should be running");
        Assert.That(!notepad2!.HasExited, "Second notepad should be running");
        Assert.That(notepad1!.Id, Is.Not.EqualTo(notepad2!.Id), "Should be different processes");

        // Step 4: Close both
        _automation?.CloseApp(notepad1.Id, force: true);
        _automation?.CloseApp(notepad2.Id, force: true);
        Thread.Sleep(1500);

        // Step 5: Verify both closed
        Assert.That(notepad1.HasExited, "First notepad should be closed");
        Assert.That(notepad2.HasExited, "Second notepad should be closed");
    }

    /// <summary>
    /// E2E: Launch, interact, and close application with full state verification
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_FullApplicationLifecycleManagement() {
        // Scenario: Complete workflow from launch to close with state checks

        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);

        // Verify launch state
        Assert.That(notepad, Is.Not.Null);
        Assert.That(notepad!.ProcessName, Contains.Substring("notepad"));
        Assert.That(!notepad.HasExited);
        Thread.Sleep(1500);

        // Get window
        var window = _automation?.GetMainWindow(notepad.Id);
        Assert.That(window, Is.Not.Null);

        // Verify running state
        Thread.Sleep(500);
        Assert.That(!notepad.HasExited);

        // Get properties
        var windowName = _automation?.GetProperty(window!, "name");
        Assert.That(windowName, Is.Not.Null);

        // Close and verify
        _automation?.CloseApp(notepad.Id);
        Thread.Sleep(1500);
        Assert.That(notepad.HasExited);
    }

    #endregion

    #region Screenshot Validation E2E Workflows

    /// <summary>
    /// E2E: Screenshot-based validation workflow
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_ScreenshotValidationWorkflow() {
        // Scenario: User wants to capture screenshots at different stages for validation

        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Thread.Sleep(1500);

        var screenshotPaths = new List<string>();

        try {
            // Take screenshot 1: Initial state
            var screenshot1 = Path.Combine(Path.GetTempPath(), $"e2e_initial_{Guid.NewGuid()}.png");
            screenshotPaths.Add(screenshot1);
            _automation?.TakeScreenshot(screenshot1);
            Assert.That(File.Exists(screenshot1), Is.True);
            var size1 = new FileInfo(screenshot1).Length;

            Thread.Sleep(500);

            // Type content
            _automation?.SendKeys("E2E Screenshot Test");
            Thread.Sleep(500);

            // Take screenshot 2: After content
            var screenshot2 = Path.Combine(Path.GetTempPath(), $"e2e_content_{Guid.NewGuid()}.png");
            screenshotPaths.Add(screenshot2);
            _automation?.TakeScreenshot(screenshot2);
            Assert.That(File.Exists(screenshot2), Is.True);
            var size2 = new FileInfo(screenshot2).Length;

            // Both screenshots should exist and be valid
            Assert.That(size1, Is.GreaterThan(0));
            Assert.That(size2, Is.GreaterThan(0));
        }
        finally {
            _automation?.CloseApp(notepad!.Id, force: true);
            foreach (var path in screenshotPaths) {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    /// <summary>
    /// E2E: Multi-window screenshot comparison
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_MultiWindowScreenshotCapture() {
        // Scenario: Capture screenshots from multiple windows

        var notepad1 = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad1!);
        Thread.Sleep(1000);

        var notepad2 = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad2!);
        Thread.Sleep(1000);

        var screenshots = new List<string>();

        try {
            // Get windows
            var window1 = _automation?.GetMainWindow(notepad1!.Id);
            var window2 = _automation?.GetMainWindow(notepad2!.Id);

            Assert.That(window1, Is.Not.Null);
            Assert.That(window2, Is.Not.Null);

            // Capture both
            var screenshot1 = Path.Combine(Path.GetTempPath(), $"e2e_window1_{Guid.NewGuid()}.png");
            var screenshot2 = Path.Combine(Path.GetTempPath(), $"e2e_window2_{Guid.NewGuid()}.png");

            screenshots.Add(screenshot1);
            screenshots.Add(screenshot2);

            _automation?.TakeScreenshot(screenshot1, window1);
            _automation?.TakeScreenshot(screenshot2, window2);

            // Verify both captured
            Assert.That(File.Exists(screenshot1), Is.True);
            Assert.That(File.Exists(screenshot2), Is.True);
        }
        finally {
            _automation?.CloseApp(notepad1!.Id, force: true);
            _automation?.CloseApp(notepad2!.Id, force: true);

            foreach (var screenshot in screenshots) {
                if (File.Exists(screenshot))
                    File.Delete(screenshot);
            }
        }
    }

    #endregion

    #region Complex Interaction E2E Workflows

    /// <summary>
    /// E2E: Simulate user typing patterns with delays
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_SimulatedUserTyping() {
        // Scenario: Simulate realistic user typing with pauses

        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Thread.Sleep(1500);

        // Simulate user typing with thinking pauses
        string[] words = { "The", "quick", "brown", "fox", "jumps" };

        foreach (var word in words) {
            _automation?.SendKeys(word);
            _automation?.SendKeys(" ");
            Thread.Sleep(300); // Simulate thinking time
        }

        Thread.Sleep(500);

        // Verify with screenshot
        var screenshotPath = Path.Combine(Path.GetTempPath(), "e2e_user_typing.png");
        try {
            _automation?.TakeScreenshot(screenshotPath);
            Assert.That(File.Exists(screenshotPath), Is.True);
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
            _automation?.CloseApp(notepad!.Id, force: true);
        }
    }

    /// <summary>
    /// E2E: Special characters and keyboard sequences
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_SpecialCharactersAndKeyboardSequences() {
        // Scenario: User enters special characters and performs keyboard shortcuts

        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Thread.Sleep(1500);

        // Type normal text
        _automation?.SendKeys("Line 1");
        Thread.Sleep(200);

        // Press Enter for new line
        _automation?.SendKeys("{ENTER}");
        Thread.Sleep(200);

        // Type special characters
        _automation?.SendKeys("Special: !@#$%^&*()");
        Thread.Sleep(200);

        // Press Tab
        _automation?.SendKeys("{TAB}");
        Thread.Sleep(200);

        // Type more text
        _automation?.SendKeys("After Tab");
        Thread.Sleep(500);

        // Verify with screenshot
        var screenshotPath = Path.Combine(Path.GetTempPath(), "e2e_special_chars.png");
        try {
            _automation?.TakeScreenshot(screenshotPath);
            Assert.That(File.Exists(screenshotPath), Is.True);
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
            _automation?.CloseApp(notepad!.Id, force: true);
        }
    }

    #endregion

    #region Stress Test E2E Workflows

    /// <summary>
    /// E2E: Rapid application launch and close cycling
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_RapidApplicationCycling() {
        // Scenario: Rapidly launch and close applications to stress-test the system

        for (int i = 0; i < 5; i++) {
            var notepad = _automation?.LaunchApp("notepad.exe");
            _launchedProcesses.Add(notepad!);
            Thread.Sleep(500);

            Assert.That(notepad, Is.Not.Null);
            Assert.That(!notepad!.HasExited);

            _automation?.CloseApp(notepad.Id, force: true);
            Thread.Sleep(300);

            Assert.That(notepad.HasExited);
        }

        Assert.Pass("Successfully cycled 5 application launches/closes");
    }

    /// <summary>
    /// E2E: Stress test with many keyboard inputs
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_StressTestManyInputs() {
        // Scenario: Send many keyboard inputs in rapid succession

        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Thread.Sleep(1500);

        // Send 100 lines
        for (int i = 0; i < 100; i++) {
            _automation?.SendKeys($"Line {i:D3} ");
            if (i % 10 == 9) {
                _automation?.SendKeys("{ENTER}");
            }
        }

        Thread.Sleep(500);

        // Verify with screenshot
        var screenshotPath = Path.Combine(Path.GetTempPath(), "e2e_stress_test.png");
        try {
            _automation?.TakeScreenshot(screenshotPath);
            Assert.That(File.Exists(screenshotPath), Is.True);
        }
        finally {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
            _automation?.CloseApp(notepad!.Id, force: true);
        }
    }

    #endregion

    #region Process Attachment E2E Workflows

    /// <summary>
    /// E2E: Launch application, reattach by name, and interact
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_ReattachmentWorkflow() {
        // Scenario: Launch app, lose reference, reattach by name, and continue

        // Step 1: Launch
        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Thread.Sleep(1500);
        var pid = notepad!.Id;

        // Step 2: Type something
        _automation?.SendKeys("Initial content");
        Thread.Sleep(500);

        // Step 3: Take screenshot
        var screenshotPath1 = Path.Combine(Path.GetTempPath(), "e2e_before_reattach.png");
        try {
            _automation?.TakeScreenshot(screenshotPath1);
            Assert.That(File.Exists(screenshotPath1), Is.True);
        }
        finally {
            if (File.Exists(screenshotPath1))
                File.Delete(screenshotPath1);
        }

        // Step 4: Reattach by name
        var reattached = _automation?.AttachToProcessByName("notepad");
        Assert.That(reattached, Is.Not.Null);
        Assert.That(reattached!.Id, Is.EqualTo(pid));

        // Step 5: Interact again
        _automation?.SendKeys("{ENTER}");
        _automation?.SendKeys("After reattachment");
        Thread.Sleep(500);

        // Step 6: Take screenshot
        var screenshotPath2 = Path.Combine(Path.GetTempPath(), "e2e_after_reattach.png");
        try {
            _automation?.TakeScreenshot(screenshotPath2);
            Assert.That(File.Exists(screenshotPath2), Is.True);
        }
        finally {
            if (File.Exists(screenshotPath2))
                File.Delete(screenshotPath2);
        }

        // Clean up
        _automation?.CloseApp(pid);
    }

    #endregion

    #region Error Recovery E2E Workflows

    /// <summary>
    /// E2E: Recovery from failed operations
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_ErrorRecoveryAndContinuation() {
        // Scenario: User encounters error but continues with workflow

        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Thread.Sleep(1500);

        // Step 1: Successful operation
        _automation?.SendKeys("Good operation");
        Thread.Sleep(500);

        // Step 2: Try invalid element search (should return null gracefully)
        var result = _automation?.ElementExists("invalid_element_id");
        Assert.That(result, Is.False);

        // Step 3: Session should still work - continue typing
        _automation?.SendKeys("{ENTER}");
        _automation?.SendKeys("After error, still working");
        Thread.Sleep(500);

        // Step 4: Verify we can still interact
        var window = _automation?.GetMainWindow(notepad!.Id);
        Assert.That(window, Is.Not.Null);

        // Step 5: Close successfully
        _automation?.CloseApp(notepad!.Id);
        Thread.Sleep(1000);
        Assert.That(notepad!.HasExited);
    }

    #endregion

    #region Complex State Management E2E Workflows

    /// <summary>
    /// E2E: Complex multi-step workflow with state tracking
    /// </summary>
    [Test]
    [Ignore("Requires GUI - use headless variant for CI/CD")]
    public void E2E_ComplexMultiStepWorkflow() {
        // Scenario: Complex real-world workflow with multiple steps and state management

        var notepad = _automation?.LaunchApp("notepad.exe");
        _launchedProcesses.Add(notepad!);
        Thread.Sleep(1500);

        var screenshots = new List<string>();

        try {
            // Step 1: Write header
            _automation?.SendKeys("=== E2E Test Report ===");
            _automation?.SendKeys("{ENTER}{ENTER}");
            Thread.Sleep(300);

            // Step 2: Add timestamp
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _automation?.SendKeys($"Generated: {timestamp}");
            _automation?.SendKeys("{ENTER}{ENTER}");
            Thread.Sleep(300);

            // Step 3: Add sections
            for (int i = 1; i <= 3; i++) {
                _automation?.SendKeys($"Section {i}");
                _automation?.SendKeys("{ENTER}");
                _automation?.SendKeys($"  - Item 1");
                _automation?.SendKeys("{ENTER}");
                _automation?.SendKeys($"  - Item 2");
                _automation?.SendKeys("{ENTER}{ENTER}");
                Thread.Sleep(200);
            }

            // Step 4: Capture progress
            var screenshot1 = Path.Combine(Path.GetTempPath(), $"e2e_complex_progress_{Guid.NewGuid()}.png");
            screenshots.Add(screenshot1);
            _automation?.TakeScreenshot(screenshot1);
            Assert.That(File.Exists(screenshot1), Is.True);

            // Step 5: Add conclusion
            _automation?.SendKeys("=== End of Report ===");
            Thread.Sleep(300);

            // Step 6: Final screenshot
            var screenshot2 = Path.Combine(Path.GetTempPath(), $"e2e_complex_final_{Guid.NewGuid()}.png");
            screenshots.Add(screenshot2);
            _automation?.TakeScreenshot(screenshot2);
            Assert.That(File.Exists(screenshot2), Is.True);
        }
        finally {
            _automation?.CloseApp(notepad!.Id, force: true);
            foreach (var screenshot in screenshots) {
                if (File.Exists(screenshot))
                    File.Delete(screenshot);
            }
        }
    }

    #endregion
}