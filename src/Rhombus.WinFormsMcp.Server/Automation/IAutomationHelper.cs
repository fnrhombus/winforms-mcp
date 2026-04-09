using System;
using System.Diagnostics;
using System.Threading.Tasks;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Interface for WinForms UI automation
/// </summary>
public interface IAutomationHelper : IDisposable {
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

    /// <summary>
    /// Get the status of a process including whether it is running, responding, exit code, and captured stderr.
    /// </summary>
    Dictionary<string, object?> GetProcessStatus(int pid);

    /// <summary>
    /// Select an item in a combo box, list box, or similar selection control.
    /// Handles the expand-find-select pattern automatically.
    /// </summary>
    /// <param name="element">The selection control element (combo box, list box, etc.)</param>
    /// <param name="value">Text of the item to select (optional if index provided)</param>
    /// <param name="index">Zero-based index of the item to select (optional if value provided)</param>
    /// <returns>The text of the selected item</returns>
    string SelectItem(AutomationElement element, string? value = null, int? index = null);

    /// <summary>
    /// Navigate and click a menu item in a menu bar or context menu.
    /// </summary>
    /// <param name="menuPath">Array of menu item names forming the path (e.g. ["File", "Save As"])</param>
    /// <param name="pid">Optional process ID to scope the search to a specific window</param>
    void ClickMenuItem(string[] menuPath, int? pid = null);

    /// <summary>
    /// Build a tree of UI automation elements starting from a root element.
    /// Each node includes name, controlType, automationId, boundingRectangle, isEnabled, isOffscreen, and children.
    /// </summary>
    /// <param name="root">Root element to start traversal from</param>
    /// <param name="depth">Maximum depth to traverse (default 3)</param>
    /// <param name="maxElements">Maximum total elements to include (default 50)</param>
    /// <param name="cacheElement">Optional callback to cache each discovered element; returns the assigned elementId</param>
    List<Dictionary<string, object?>> GetElementTree(AutomationElement root, int depth = 3, int maxElements = 50, Func<AutomationElement, string>? cacheElement = null);
}