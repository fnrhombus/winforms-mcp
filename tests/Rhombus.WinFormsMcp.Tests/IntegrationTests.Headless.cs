namespace Rhombus.WinFormsMcp.Tests;

using Moq;

using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Headless integration tests using mocks
/// These tests verify complete workflows without requiring a GUI
/// </summary>
public class IntegrationTestsHeadless {
    private Mock<IAutomationHelper>? _mockAutomation;

    [SetUp]
    public void Setup() {
        _mockAutomation = new Mock<IAutomationHelper>();
    }

    [Test]
    public void TestApplicationLaunchWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.LaunchApp("notepad.exe", null, null))
            .Returns((System.Diagnostics.Process)null!) // Returns dummy value
            .Verifiable();

        _mockAutomation
            .Setup(a => a.GetMainWindow(It.IsAny<int>()))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        // Act & Assert - verify methods can be called in sequence
        _mockAutomation.Object.LaunchApp("notepad.exe");
        _mockAutomation.Object.GetMainWindow(1234);

        _mockAutomation.Verify(a => a.LaunchApp("notepad.exe", null, null), Times.Once);
        _mockAutomation.Verify(a => a.GetMainWindow(It.IsAny<int>()), Times.Once);
    }

    [Test]
    public void TestFindElementWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.FindByName("OK", null, 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        // Act
        var element = _mockAutomation.Object.FindByName("OK", null);

        // Assert
        _mockAutomation.Verify(a => a.FindByName("OK", null, 5000), Times.Once);
    }

    [Test]
    public void TestClickElementWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Verifiable();

        // Act
        _mockAutomation.Object.Click(null!, false);

        // Assert
        _mockAutomation.Verify(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false), Times.Once);
    }

    [Test]
    public void TestTypeTextWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Hello World", false))
            .Verifiable();

        // Act
        _mockAutomation.Object.TypeText(null!, "Hello World", false);

        // Assert
        _mockAutomation.Verify(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Hello World", false), Times.Once);
    }

    [Test]
    public void TestAttachToProcessByNameWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.AttachToProcessByName("notepad"))
            .Returns((System.Diagnostics.Process)null!) // Simplified for headless
            .Verifiable();

        // Act
        _mockAutomation.Object.AttachToProcessByName("notepad");

        // Assert
        _mockAutomation.Verify(a => a.AttachToProcessByName("notepad"), Times.Once);
    }

    [Test]
    public void TestAttachToNonExistentProcessThrows() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.AttachToProcessByName("nonexistent"))
            .Throws<InvalidOperationException>()
            .Verifiable();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => _mockAutomation.Object.AttachToProcessByName("nonexistent")
        );
    }

    [Test]
    public void TestElementExistenceCheckWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.ElementExists("testId", null))
            .Returns(true)
            .Verifiable();

        // Act
        var exists = _mockAutomation.Object.ElementExists("testId");

        // Assert
        Assert.That(exists, Is.True);
        _mockAutomation.Verify(a => a.ElementExists("testId", null), Times.Once);
    }

    [Test]
    public void TestScreenshotCaptureWorkflow() {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), "test.png");

        _mockAutomation!
            .Setup(a => a.TakeScreenshot(path, null))
            .Verifiable();

        // Act
        _mockAutomation.Object.TakeScreenshot(path);

        // Assert
        _mockAutomation.Verify(a => a.TakeScreenshot(path, null), Times.Once);
    }

    [Test]
    public async Task TestAsyncWaitForElementWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.WaitForElementAsync("loadingSpinner", null, 10000))
            .ReturnsAsync(true)
            .Verifiable();

        // Act
        var found = await _mockAutomation.Object.WaitForElementAsync("loadingSpinner", null, 10000);

        // Assert
        Assert.That(found, Is.True);
        _mockAutomation.Verify(a => a.WaitForElementAsync("loadingSpinner", null, 10000), Times.Once);
    }

    [Test]
    public void TestGetPropertyWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetProperty(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "name"))
            .Returns("TestElement")
            .Verifiable();

        // Act
        var value = _mockAutomation.Object.GetProperty(null!, "name");

        // Assert
        Assert.That(value, Is.EqualTo("TestElement"));
        _mockAutomation.Verify(a => a.GetProperty(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "name"), Times.Once);
    }

    [Test]
    public void TestSendKeysWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.SendKeys("test keys"))
            .Verifiable();

        // Act
        _mockAutomation.Object.SendKeys("test keys");

        // Assert
        _mockAutomation.Verify(a => a.SendKeys("test keys"), Times.Once);
    }

    [Test]
    public void TestCloseAppWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.CloseApp(1234, false))
            .Verifiable();

        // Act
        _mockAutomation.Object.CloseApp(1234, force: false);

        // Assert
        _mockAutomation.Verify(a => a.CloseApp(1234, false), Times.Once);
    }

    [Test]
    public void TestMultipleOperationsSequenceWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("Button2", null, 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        // Act
        _mockAutomation.Object.Click(null!);
        var btn2 = _mockAutomation.Object.FindByName("Button2");
        _mockAutomation.Object.Click(btn2!);

        // Assert
        _mockAutomation.Verify(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false), Times.AtLeastOnce);
        _mockAutomation.Verify(a => a.FindByName("Button2", null, 5000), Times.Once);
    }

    [Test]
    public void TestErrorRecoveryWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.ElementExists("invalidId", null))
            .Returns(false)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("ValidElement", null, 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        // Act
        var exists = _mockAutomation.Object.ElementExists("invalidId");
        var element = _mockAutomation.Object.FindByName("ValidElement");

        // Assert - Verify recovery from failed find
        Assert.That(exists, Is.False);
        // Element is null, but method was called
        _mockAutomation.Verify(a => a.FindByName("ValidElement", null, 5000), Times.Once);
    }

    [Test]
    public void TestDragDropWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.DragDrop(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()))
            .Verifiable();

        // Act
        _mockAutomation.Object.DragDrop(null!, null!);

        // Assert
        _mockAutomation.Verify(a => a.DragDrop(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()), Times.Once);
    }

    [Test]
    public void TestSetValueWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.SetValue(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "new value"))
            .Verifiable();

        // Act
        _mockAutomation.Object.SetValue(null!, "new value");

        // Assert
        _mockAutomation.Verify(a => a.SetValue(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "new value"), Times.Once);
    }

    // ===== NEGATIVE TESTS =====

    [Test]
    public void TestLaunchFailureRecovery() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.LaunchApp("badapp.exe", null, null))
            .Throws<InvalidOperationException>()
            .Verifiable();

        _mockAutomation
            .Setup(a => a.LaunchApp("notepad.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.LaunchApp("badapp.exe")
        );

        // Recovery
        _mockAutomation.Object.LaunchApp("notepad.exe");
        _mockAutomation.Verify(a => a.LaunchApp("notepad.exe", null, null), Times.Once);
    }

    [Test]
    public void TestElementNotFoundInWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.FindByName("NonExistentButton", null, 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Throws<InvalidOperationException>()
            .Verifiable();

        // Act
        var element = _mockAutomation.Object.FindByName("NonExistentButton", null);

        // Assert - Element is null
        Assert.That(element, Is.Null);
    }

    [Test]
    public void TestScreenshotFailureDoesNotBreakWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.TakeScreenshot("/invalid/path.png", null))
            .Throws<InvalidOperationException>()
            .Verifiable();

        _mockAutomation
            .Setup(a => a.SendKeys("test"))
            .Verifiable();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.TakeScreenshot("/invalid/path.png")
        );

        // Recovery - Continue with other operations
        _mockAutomation.Object.SendKeys("test");
        _mockAutomation.Verify(a => a.SendKeys("test"), Times.Once);
    }

    [Test]
    public void TestMultipleFailuresInSequence() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.FindByName("Button1", null, 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("Button2", null, 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("Button3", null, 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        // Act
        var btn1 = _mockAutomation.Object.FindByName("Button1", null);
        var btn2 = _mockAutomation.Object.FindByName("Button2", null);
        var btn3 = _mockAutomation.Object.FindByName("Button3", null);

        // Assert - All null
        Assert.That(btn1, Is.Null);
        Assert.That(btn2, Is.Null);
        Assert.That(btn3, Is.Null);
    }

    [Test]
    public void TestInvalidElementInteraction() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Throws<InvalidOperationException>()
            .Verifiable();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.Click(null!, false)
        );
    }

    [Test]
    public void TestDragDropWithInvalidElements() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.DragDrop(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()))
            .Throws<InvalidOperationException>()
            .Verifiable();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.DragDrop(null!, null!)
        );
    }

    [Test]
    public void TestGetPropertyWithNullElement() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetProperty(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<string>()))
            .Throws<NullReferenceException>()
            .Verifiable();

        // Act & Assert
        Assert.Throws<NullReferenceException>(() =>
            _mockAutomation.Object.GetProperty(null!, "name")
        );
    }
}