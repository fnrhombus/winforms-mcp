using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Interface for WinForms UI automation
/// </summary>
public interface IAutomationHelper : IDisposable
{
    /// <summary>
    /// Whether launched applications should run headless (hidden window)
    /// </summary>
    bool Headless { get; }

    /// <summary>
    /// Launch a WinForms application
    /// </summary>
    Process LaunchApp(string path, string? arguments = null, string? workingDirectory = null);

    /// <summary>
    /// Attach to a running process
    /// </summary>
    Process AttachToProcess(int pid);

    /// <summary>
    /// Attach to a running process by name
    /// </summary>
    Process AttachToProcessByName(string name);

    /// <summary>
    /// Get main window element of a process
    /// </summary>
    AutomationElement? GetMainWindow(int pid);

    /// <summary>
    /// Find element by AutomationId
    /// </summary>
    AutomationElement? FindByAutomationId(string automationId, AutomationElement? parent = null, int timeoutMs = 5000);

    /// <summary>
    /// Find element by Name
    /// </summary>
    AutomationElement? FindByName(string name, AutomationElement? parent = null, int timeoutMs = 5000);

    /// <summary>
    /// Find element by ClassName
    /// </summary>
    AutomationElement? FindByClassName(string className, AutomationElement? parent = null, int timeoutMs = 5000);

    /// <summary>
    /// Find element by ControlType
    /// </summary>
    AutomationElement? FindByControlType(ControlType controlType, AutomationElement? parent = null, int timeoutMs = 5000);

    /// <summary>
    /// Find multiple elements matching condition
    /// </summary>
    AutomationElement[]? FindAll(ConditionBase condition, AutomationElement? parent = null, int timeoutMs = 5000);

    /// <summary>
    /// Check if element exists
    /// </summary>
    bool ElementExists(string automationId, AutomationElement? parent = null);

    /// <summary>
    /// Click element
    /// </summary>
    void Click(AutomationElement element, bool doubleClick = false);

    /// <summary>
    /// Type text into element
    /// </summary>
    void TypeText(AutomationElement element, string text, bool clearFirst = false);

    /// <summary>
    /// Set value on element
    /// </summary>
    void SetValue(AutomationElement element, string value);

    /// <summary>
    /// Get element property
    /// </summary>
    object? GetProperty(AutomationElement element, string propertyName);

    /// <summary>
    /// Take screenshot of element or full desktop
    /// </summary>
    void TakeScreenshot(string outputPath, AutomationElement? element = null);

    /// <summary>
    /// Drag and drop
    /// </summary>
    void DragDrop(AutomationElement source, AutomationElement target);

    /// <summary>
    /// Send keyboard keys
    /// </summary>
    void SendKeys(string keys);

    /// <summary>
    /// Close application
    /// </summary>
    void CloseApp(int pid, bool force = false);

    /// <summary>
    /// Wait for element to appear
    /// </summary>
    Task<bool> WaitForElementAsync(string automationId, AutomationElement? parent = null, int timeoutMs = 10000);

    /// <summary>
    /// Get all child elements
    /// </summary>
    AutomationElement[]? GetAllChildren(AutomationElement element);
}
