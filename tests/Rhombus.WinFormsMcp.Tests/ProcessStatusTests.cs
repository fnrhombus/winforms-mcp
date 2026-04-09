namespace Rhombus.WinFormsMcp.Tests;

using System.Collections.Generic;
using System.Diagnostics;

using Moq;

using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Tests for the get_process_status tool and stderr capture in launch_app.
/// Uses a mix of mock-based tests (for interface compliance) and real process tests
/// (for verifying actual stderr capture and process status).
/// </summary>
public class ProcessStatusTests {
    private Mock<IAutomationHelper>? _mockAutomation;

    [SetUp]
    public void Setup() {
        _mockAutomation = new Mock<IAutomationHelper>();
    }

    [TearDown]
    public void TearDown() {
        _mockAutomation?.VerifyAll();
    }

    // ===== Interface / Mock Tests =====

    [Test]
    public void TestGetProcessStatusViaInterface() {
        // Arrange
        var expectedStatus = new Dictionary<string, object?> {
            ["isRunning"] = true,
            ["hasExited"] = false,
            ["exitCode"] = null,
            ["responding"] = true,
            ["mainWindowTitle"] = "Test Window",
            ["stderr"] = ""
        };

        _mockAutomation!
            .Setup(a => a.GetProcessStatus(1234))
            .Returns(expectedStatus)
            .Verifiable();

        // Act
        var status = _mockAutomation.Object.GetProcessStatus(1234);

        // Assert
        Assert.That(status["isRunning"], Is.EqualTo(true));
        Assert.That(status["hasExited"], Is.EqualTo(false));
        Assert.That(status["exitCode"], Is.Null);
        Assert.That(status["responding"], Is.EqualTo(true));
        Assert.That(status["mainWindowTitle"], Is.EqualTo("Test Window"));
        Assert.That(status["stderr"], Is.EqualTo(""));
    }

    [Test]
    public void TestGetProcessStatusForExitedProcess() {
        // Arrange
        var expectedStatus = new Dictionary<string, object?> {
            ["isRunning"] = false,
            ["hasExited"] = true,
            ["exitCode"] = 1,
            ["responding"] = false,
            ["mainWindowTitle"] = "",
            ["stderr"] = "Error: something went wrong\n"
        };

        _mockAutomation!
            .Setup(a => a.GetProcessStatus(5678))
            .Returns(expectedStatus)
            .Verifiable();

        // Act
        var status = _mockAutomation.Object.GetProcessStatus(5678);

        // Assert
        Assert.That(status["isRunning"], Is.EqualTo(false));
        Assert.That(status["hasExited"], Is.EqualTo(true));
        Assert.That(status["exitCode"], Is.EqualTo(1));
        Assert.That(status["responding"], Is.EqualTo(false));
        Assert.That(status["stderr"], Is.EqualTo("Error: something went wrong\n"));
    }

    // ===== Real Process Tests =====

    [Test]
    public void TestGetProcessStatusOfRunningProcess() {
        // Arrange - launch a real process that stays alive briefly
        using var automation = new AutomationHelper(headless: true);
        var process = automation.LaunchApp("cmd.exe", "/c ping 127.0.0.1 -n 5 >nul");

        try {
            // Act
            var status = automation.GetProcessStatus(process.Id);

            // Assert
            Assert.That(status["isRunning"], Is.EqualTo(true));
            Assert.That(status["hasExited"], Is.EqualTo(false));
            Assert.That(status["exitCode"], Is.Null);
            Assert.That(status["mainWindowTitle"], Is.Not.Null);
            Assert.That(status["stderr"], Is.Not.Null);
        }
        finally {
            try { process.Kill(); }
            catch { }
            process.WaitForExit(5000);
        }
    }

    [Test]
    public void TestGetProcessStatusAfterExit() {
        // Arrange - launch a process that exits immediately with code 0
        using var automation = new AutomationHelper(headless: true);
        var process = automation.LaunchApp("cmd.exe", "/c exit 0");
        process.WaitForExit(5000);

        // Act
        var status = automation.GetProcessStatus(process.Id);

        // Assert
        Assert.That(status["isRunning"], Is.EqualTo(false));
        Assert.That(status["hasExited"], Is.EqualTo(true));
        Assert.That(status["exitCode"], Is.EqualTo(0));
        Assert.That(status["responding"], Is.EqualTo(false));
    }

    [Test]
    public void TestGetProcessStatusAfterExitWithNonZeroCode() {
        // Arrange - launch a process that exits with error code
        using var automation = new AutomationHelper(headless: true);
        var process = automation.LaunchApp("cmd.exe", "/c exit 42");
        process.WaitForExit(5000);

        // Act
        var status = automation.GetProcessStatus(process.Id);

        // Assert
        Assert.That(status["isRunning"], Is.EqualTo(false));
        Assert.That(status["hasExited"], Is.EqualTo(true));
        Assert.That(status["exitCode"], Is.EqualTo(42));
    }

    [Test]
    public void TestStderrCapture() {
        // Arrange - launch a process that writes to stderr
        using var automation = new AutomationHelper(headless: true);
        var process = automation.LaunchApp("cmd.exe", "/c echo error message 1>&2");
        process.WaitForExit(5000);
        // Give a small delay for async stderr to be captured
        Thread.Sleep(200);

        // Act
        var status = automation.GetProcessStatus(process.Id);

        // Assert
        var stderr = status["stderr"] as string;
        Assert.That(stderr, Is.Not.Null);
        Assert.That(stderr!, Does.Contain("error message"));
    }

    [Test]
    public void TestGetProcessStatusForUnknownPid() {
        // Arrange - use a PID that definitely does not exist
        using var automation = new AutomationHelper(headless: true);

        // Act
        var status = automation.GetProcessStatus(99999);

        // Assert
        Assert.That(status["isRunning"], Is.EqualTo(false));
        Assert.That(status["hasExited"], Is.EqualTo(true));
        Assert.That(status["exitCode"], Is.Null);
        Assert.That(status["responding"], Is.EqualTo(false));
        Assert.That(status["mainWindowTitle"], Is.EqualTo(""));
        Assert.That(status["stderr"], Is.EqualTo(""));
    }

    [Test]
    public void TestGetStderrReturnsEmptyForUnknownPid() {
        // Arrange
        using var automation = new AutomationHelper(headless: true);

        // Act
        var stderr = automation.GetStderr(99999);

        // Assert
        Assert.That(stderr, Is.EqualTo(""));
    }

    [Test]
    public void TestStderrBufferCleanedOnCloseApp() {
        // Arrange - launch a process, then close it
        using var automation = new AutomationHelper(headless: true);
        var process = automation.LaunchApp("cmd.exe", "/c echo err 1>&2");
        process.WaitForExit(5000);
        Thread.Sleep(200);

        var pid = process.Id;

        // Verify stderr was captured
        var stderrBefore = automation.GetStderr(pid);
        Assert.That(stderrBefore, Does.Contain("err"));

        // Act - close the app (cleans up buffers)
        automation.CloseApp(pid, force: true);

        // Assert - stderr buffer should be cleaned up
        var stderrAfter = automation.GetStderr(pid);
        Assert.That(stderrAfter, Is.EqualTo(""));
    }
}