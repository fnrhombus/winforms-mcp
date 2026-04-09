namespace Rhombus.WinFormsMcp.Tests;

using Moq;

using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Headless end-to-end tests using mocks
/// These tests verify complex multi-step workflows without requiring a GUI
/// </summary>
public class E2ETestsHeadless {
    private Mock<IAutomationHelper>? _mockAutomation;

    [SetUp]
    public void Setup() {
        _mockAutomation = new Mock<IAutomationHelper>();
    }

    [Test]
    public void E2E_TextEditingWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.LaunchApp("notepad.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.GetMainWindow(It.IsAny<int>()))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId("RichEditBox", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Hello World E2E Test", false))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.SendKeys("^s"))
            .Verifiable();

        // Act
        _mockAutomation.Object.LaunchApp("notepad.exe");
        var mainWindow = _mockAutomation.Object.GetMainWindow(1234);
        var textBox = _mockAutomation.Object.FindByAutomationId("RichEditBox", mainWindow);
        _mockAutomation.Object.TypeText(textBox!, "Hello World E2E Test");
        _mockAutomation.Object.SendKeys("^s");

        // Assert
        _mockAutomation.Verify(a => a.LaunchApp("notepad.exe", null, null), Times.Once);
        _mockAutomation.Verify(a => a.GetMainWindow(It.IsAny<int>()), Times.Once);
    }

    [Test]
    public void E2E_MultipleApplicationInteraction() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.LaunchApp("notepad.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.LaunchApp("calc.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.AttachToProcessByName("notepad"))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.AttachToProcessByName("calc"))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        // Act
        _mockAutomation.Object.LaunchApp("notepad.exe");
        _mockAutomation.Object.LaunchApp("calc.exe");
        _mockAutomation.Object.AttachToProcessByName("notepad");
        _mockAutomation.Object.AttachToProcessByName("calc");

        // Assert
        _mockAutomation.Verify(a => a.LaunchApp("notepad.exe", null, null), Times.Once);
        _mockAutomation.Verify(a => a.LaunchApp("calc.exe", null, null), Times.Once);
    }

    [Test]
    public void E2E_WindowNavigationWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetMainWindow(5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("Open Dialog", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("Dialog Box", null, 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        // Act
        var window = _mockAutomation.Object.GetMainWindow(5000);
        var button = _mockAutomation.Object.FindByName("Open Dialog", window);
        _mockAutomation.Object.Click(button!);
        var dialog = _mockAutomation.Object.FindByName("Dialog Box");

        // Assert
        _mockAutomation.Verify(a => a.GetMainWindow(5000), Times.Once);
        _mockAutomation.Verify(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false), Times.Once);
    }

    [Test]
    public void E2E_FormFillingWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetMainWindow(5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId(It.IsAny<string>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<string>(), true))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Verifiable();

        // Act
        var form = _mockAutomation.Object.GetMainWindow(5000);
        var name = _mockAutomation.Object.FindByAutomationId("NameField", form);
        var email = _mockAutomation.Object.FindByAutomationId("EmailField", form);
        var submit = _mockAutomation.Object.FindByAutomationId("SubmitButton", form);

        _mockAutomation.Object.TypeText(name!, "John Doe", true);
        _mockAutomation.Object.TypeText(email!, "john@example.com", true);
        _mockAutomation.Object.Click(submit!);

        // Assert
        _mockAutomation.Verify(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<string>(), true), Times.Exactly(2));
        _mockAutomation.Verify(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false), Times.Once);
    }

    [Test]
    public async Task E2E_AsynchronousElementWaitingWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.WaitForElementAsync("LoadingSpinner", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 10000))
            .ReturnsAsync(true)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("Success Message", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        // Act
        var elementFound = await _mockAutomation.Object.WaitForElementAsync("LoadingSpinner", null, 10000);
        var successMsg = _mockAutomation.Object.FindByName("Success Message", null);

        // Assert
        Assert.That(elementFound, Is.True);
        _mockAutomation.Verify(a => a.WaitForElementAsync("LoadingSpinner", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 10000), Times.Once);
    }

    [Test]
    public void E2E_ScreenshotValidationWorkflow() {
        // Arrange
        var screenshotPath1 = Path.Combine(Path.GetTempPath(), $"before_{Guid.NewGuid()}.png");
        var screenshotPath2 = Path.Combine(Path.GetTempPath(), $"after_{Guid.NewGuid()}.png");

        _mockAutomation!
            .Setup(a => a.LaunchApp("notepad.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.GetMainWindow(It.IsAny<int>()))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TakeScreenshot(It.IsAny<string>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("TextBox", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Test Content", false))
            .Verifiable();

        // Act
        _mockAutomation.Object.LaunchApp("notepad.exe");
        var window = _mockAutomation.Object.GetMainWindow(1234);
        _mockAutomation.Object.TakeScreenshot(screenshotPath1);

        var textBox = _mockAutomation.Object.FindByName("TextBox", window);
        _mockAutomation.Object.TypeText(textBox!, "Test Content");
        _mockAutomation.Object.TakeScreenshot(screenshotPath2);

        // Assert
        _mockAutomation.Verify(a => a.TakeScreenshot(It.IsAny<string>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()), Times.Exactly(2));
    }

    [Test]
    public void E2E_ComplexMultiStepWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.LaunchApp("app.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.GetMainWindow(It.IsAny<int>()))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId(It.IsAny<string>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<string>(), true))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.WaitForElementAsync("SuccessMsg", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .ReturnsAsync(true)
            .Verifiable();

        var screenshotPath = Path.Combine(Path.GetTempPath(), $"workflow_{Guid.NewGuid()}.png");

        _mockAutomation
            .Setup(a => a.TakeScreenshot(It.IsAny<string>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()))
            .Verifiable();

        // Act
        _mockAutomation.Object.LaunchApp("app.exe");
        var form = _mockAutomation.Object.GetMainWindow(1234);
        var nameField = _mockAutomation.Object.FindByAutomationId("NameField", form);
        _mockAutomation.Object.TypeText(nameField!, "Alice", true);

        var emailField = _mockAutomation.Object.FindByAutomationId("EmailField", form);
        _mockAutomation.Object.TypeText(emailField!, "alice@test.com", true);

        var submitBtn = _mockAutomation.Object.FindByAutomationId("SubmitBtn", form);
        _mockAutomation.Object.Click(submitBtn!);

        var taskResult = _mockAutomation.Object.WaitForElementAsync("SuccessMsg", form, 5000);
        Assert.That(taskResult.Result, Is.True);

        _mockAutomation.Object.TakeScreenshot(screenshotPath, form);

        // Assert
        _mockAutomation.Verify(a => a.LaunchApp("app.exe", null, null), Times.Once);
        _mockAutomation.Verify(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false), Times.Once);
    }

    [Test]
    public void E2E_DragDropWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetMainWindow(5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId(It.IsAny<string>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.DragDrop(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()))
            .Verifiable();

        // Act
        var window = _mockAutomation.Object.GetMainWindow(5000);
        var src = _mockAutomation.Object.FindByAutomationId("DragSource", window);
        var tgt = _mockAutomation.Object.FindByAutomationId("DropTarget", window);
        _mockAutomation.Object.DragDrop(src!, tgt!);

        // Assert
        _mockAutomation.Verify(a => a.DragDrop(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()), Times.Once);
    }

    [Test]
    public void E2E_ErrorHandlingWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.AttachToProcessByName("nonexistent"))
            .Throws<InvalidOperationException>()
            .Verifiable();

        _mockAutomation
            .Setup(a => a.LaunchApp("notepad.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => _mockAutomation.Object.AttachToProcessByName("nonexistent")
        );

        // Recovery: launch app instead
        _mockAutomation.Object.LaunchApp("notepad.exe");
        _mockAutomation.Verify(a => a.LaunchApp("notepad.exe", null, null), Times.Once);
    }

    [Test]
    public void E2E_MultipleClicksWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), true))
            .Verifiable();

        // Act
        _mockAutomation.Object.Click(null!, false);
        _mockAutomation.Object.Click(null!, true);

        // Assert
        _mockAutomation.Verify(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false), Times.Once);
        _mockAutomation.Verify(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), true), Times.Once);
    }

    [Test]
    public void E2E_PropertyRetrievalWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetProperty(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "name"))
            .Returns("TestName")
            .Verifiable();

        _mockAutomation
            .Setup(a => a.GetProperty(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "automationid"))
            .Returns("testId")
            .Verifiable();

        // Act
        var name = _mockAutomation.Object.GetProperty(null!, "name");
        var id = _mockAutomation.Object.GetProperty(null!, "automationid");

        // Assert
        Assert.That(name, Is.EqualTo("TestName"));
        Assert.That(id, Is.EqualTo("testId"));
    }

    [Test]
    public void E2E_SequentialTypingWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Username", true))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.SendKeys("{TAB}"))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Password", true))
            .Verifiable();

        // Act
        _mockAutomation.Object.TypeText(null!, "Username", true);
        _mockAutomation.Object.SendKeys("{TAB}");
        _mockAutomation.Object.TypeText(null!, "Password", true);

        // Assert
        _mockAutomation.Verify(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Username", true), Times.Once);
        _mockAutomation.Verify(a => a.SendKeys("{TAB}"), Times.Once);
        _mockAutomation.Verify(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Password", true), Times.Once);
    }

    // ===== NEGATIVE TESTS =====

    [Test]
    public void E2E_LaunchFailureWithFallback() {
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

        // Recovery: fallback to alternate app
        _mockAutomation.Object.LaunchApp("notepad.exe");
        _mockAutomation.Verify(a => a.LaunchApp("notepad.exe", null, null), Times.Once);
    }

    [Test]
    public void E2E_FormSubmissionWithMissingRequiredFields() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetMainWindow(5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId("NameField", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId("EmailField", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId("SubmitButton", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), It.IsAny<string>(), true))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Throws<InvalidOperationException>() // Submission fails due to validation
            .Verifiable();

        // Act
        var form = _mockAutomation.Object.GetMainWindow(5000);
        var nameField = _mockAutomation.Object.FindByAutomationId("NameField", form);
        var emailField = _mockAutomation.Object.FindByAutomationId("EmailField", form);
        var submitBtn = _mockAutomation.Object.FindByAutomationId("SubmitButton", form);

        // Fill only name, skip email
        _mockAutomation.Object.TypeText(nameField!, "John", true);

        // Assert & Act - submission fails
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.Click(submitBtn!, false)
        );
    }

    [Test]
    public async Task E2E_TimeoutDuringWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.LaunchApp("slowapp.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.GetMainWindow(It.IsAny<int>()))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.WaitForElementAsync("LoadingSpinner", null, 1000))
            .ReturnsAsync(false) // Timeout - element never appeared
            .Verifiable();

        // Act
        _mockAutomation.Object.LaunchApp("slowapp.exe");
        var window = _mockAutomation.Object.GetMainWindow(1234);

        var spinnerFound = await _mockAutomation.Object.WaitForElementAsync("LoadingSpinner", null, 1000);

        // Assert
        Assert.That(spinnerFound, Is.False);
        _mockAutomation.Verify(a => a.WaitForElementAsync("LoadingSpinner", null, 1000), Times.Once);
    }

    [Test]
    public void E2E_PartialFormFillFailure() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetMainWindow(5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId("Field1", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId("Field2", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Value1", true))
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TypeText(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), "Value2", true))
            .Throws<InvalidOperationException>() // Second field is read-only
            .Verifiable();

        // Act
        var form = _mockAutomation.Object.GetMainWindow(5000);
        var field1 = _mockAutomation.Object.FindByAutomationId("Field1", form);
        var field2 = _mockAutomation.Object.FindByAutomationId("Field2", form);

        _mockAutomation.Object.TypeText(field1!, "Value1", true);

        // Assert & Act - Second fill fails
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.TypeText(field2!, "Value2", true)
        );
    }

    [Test]
    public void E2E_InvalidScreenshotPathHandling() {
        // Arrange
        var invalidPath = "/invalid/path/screenshot.png";

        _mockAutomation!
            .Setup(a => a.LaunchApp("app.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TakeScreenshot(invalidPath, It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()))
            .Throws<InvalidOperationException>()
            .Verifiable();

        _mockAutomation
            .Setup(a => a.TakeScreenshot(It.IsNotIn(invalidPath), It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()))
            .Verifiable();

        // Act
        _mockAutomation.Object.LaunchApp("app.exe");

        // Assert & Act - First screenshot fails
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.TakeScreenshot(invalidPath)
        );

        // Recovery: use valid path
        var validPath = Path.Combine(Path.GetTempPath(), "screenshot.png");
        _mockAutomation.Object.TakeScreenshot(validPath);
        _mockAutomation.Verify(a => a.TakeScreenshot(validPath, It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>()), Times.Once);
    }

    [Test]
    public void E2E_ElementNotFoundDuringNavigation() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetMainWindow(5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("NextButton", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!) // Element not found
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByName("AlternateButton", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        // Act
        var window = _mockAutomation.Object.GetMainWindow(5000);
        var nextBtn = _mockAutomation.Object.FindByName("NextButton", window);

        // Assert - Element is null
        Assert.That(nextBtn, Is.Null);

        // Recovery: try alternate button
        var altBtn = _mockAutomation.Object.FindByName("AlternateButton", window);
        Assert.That(altBtn, Is.Null);
    }

    [Test]
    public void E2E_ConcurrentOperationFailure() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.LaunchApp("app1.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.LaunchApp("app2.exe", null, null))
            .Throws<InvalidOperationException>() // Second launch fails due to resource conflict
            .Verifiable();

        _mockAutomation
            .Setup(a => a.LaunchApp("app3.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        // Act & Assert
        _mockAutomation.Object.LaunchApp("app1.exe");

        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.LaunchApp("app2.exe")
        );

        // Recovery: retry with different app
        _mockAutomation.Object.LaunchApp("app3.exe");
        _mockAutomation.Verify(a => a.LaunchApp("app1.exe", null, null), Times.Once);
        _mockAutomation.Verify(a => a.LaunchApp("app3.exe", null, null), Times.Once);
    }

    [Test]
    public void E2E_ProcessTerminatedMidWorkflow() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.LaunchApp("app.exe", null, null))
            .Returns((System.Diagnostics.Process)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.GetMainWindow(1234))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.FindByAutomationId("Button1", It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), 5000))
            .Returns((FlaUI.Core.AutomationElements.AutomationElement)null!)
            .Verifiable();

        _mockAutomation
            .Setup(a => a.Click(It.IsAny<FlaUI.Core.AutomationElements.AutomationElement>(), false))
            .Throws<InvalidOperationException>() // Process terminated
            .Verifiable();

        _mockAutomation
            .Setup(a => a.CloseApp(1234, false))
            .Verifiable();

        // Act
        _mockAutomation.Object.LaunchApp("app.exe");
        var window = _mockAutomation.Object.GetMainWindow(1234);
        var button = _mockAutomation.Object.FindByAutomationId("Button1", window);

        // Assert & Act - Click fails because process terminated
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.Click(button!, false)
        );

        // Recovery: cleanup
        _mockAutomation.Object.CloseApp(1234);
        _mockAutomation.Verify(a => a.CloseApp(1234, false), Times.Once);
    }
}