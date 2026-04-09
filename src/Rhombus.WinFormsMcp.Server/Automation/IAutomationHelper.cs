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
    /// Take screenshot with optional PID for PrintWindow-based capture.
    /// </summary>
    void TakeScreenshot(string outputPath, AutomationElement? element, int? pid);

    /// <summary>
    /// Drag and drop
    /// </summary>
    void DragDrop(AutomationElement source, AutomationElement target);

    /// <summary>
    /// Send keyboard keys
    /// </summary>
    void SendKeys(string keys, int? targetPid = null);

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
    /// Switch the calling thread to a process's desktop, execute an action, then restore.
    /// No-op for default desktop processes.
    /// </summary>
    T OnProcessDesktop<T>(int pid, Func<T> action);

    /// <summary>
    /// Non-generic overload.
    /// </summary>
    void OnProcessDesktop(int pid, Action action);

    /// <summary>
    /// Build a tree of UI automation elements starting from a root element.
    /// Each node includes name, controlType, automationId, boundingRectangle, isEnabled, isOffscreen, and children.
    /// </summary>
    /// <param name="root">Root element to start traversal from</param>
    /// <param name="depth">Maximum depth to traverse (default 3)</param>
    /// <param name="maxElements">Maximum total elements to include (default 50)</param>
    /// <param name="cacheElement">Optional callback to cache each discovered element; returns the assigned elementId</param>
    List<Dictionary<string, object?>> GetElementTree(AutomationElement root, int depth = 3, int maxElements = 50, Func<AutomationElement, string>? cacheElement = null);

    /// <summary>
    /// Wait for an element's property to match a condition.
    /// </summary>
    /// <param name="element">The element to poll</param>
    /// <param name="propertyName">Property name (same names as GetProperty)</param>
    /// <param name="expectedValue">The value to compare against</param>
    /// <param name="comparison">Comparison type: equals, contains, not_equals, greater_than, less_than</param>
    /// <param name="timeoutMs">Maximum wait time in milliseconds</param>
    /// <returns>Tuple of (matched, actualValue, elapsedMs)</returns>
    Task<(bool matched, string? actualValue, long elapsedMs)> WaitForConditionAsync(
        AutomationElement element, string propertyName, string expectedValue,
        string comparison = "equals", int timeoutMs = 10000);

    /// <summary>
    /// Toggle a checkbox, radio button, or toggle button using TogglePattern.
    /// </summary>
    /// <param name="element">The element to toggle</param>
    /// <param name="desiredState">Optional desired state: "on", "off", "indeterminate", or null to just toggle</param>
    /// <returns>Tuple of (previousState, currentState)</returns>
    (string previousState, string currentState) Toggle(AutomationElement element, string? desiredState = null);

    /// <summary>
    /// Scroll within a scrollable control.
    /// </summary>
    /// <param name="element">The scrollable element</param>
    /// <param name="direction">up, down, left, right</param>
    /// <param name="amount">Number of scroll units (default 1)</param>
    /// <param name="scrollType">line or page (default line)</param>
    /// <returns>Current scroll percentages and scrollability</returns>
    Dictionary<string, object> Scroll(AutomationElement element, string direction, int amount = 1, string scrollType = "line");

    /// <summary>
    /// Read data from a DataGridView or Grid control.
    /// </summary>
    Dictionary<string, object?> GetTableData(AutomationElement element, int startRow = 0, int rowCount = 50, int[]? columns = null);

    /// <summary>
    /// Set a cell value in a DataGridView or Grid control.
    /// </summary>
    (string? previousValue, string? newValue) SetTableCell(AutomationElement element, int row, int column, string value);

    /// <summary>
    /// Manage a window: maximize, minimize, restore, resize, or move.
    /// </summary>
    Dictionary<string, object?> ManageWindow(int pid, string action, int? width = null, int? height = null, int? x = null, int? y = null);

    /// <summary>
    /// List all top-level windows for a process.
    /// </summary>
    List<Dictionary<string, object?>> ListWindows(int pid);

    /// <summary>
    /// Get the currently focused element.
    /// </summary>
    AutomationElement? GetFocusedElement();

    /// <summary>
    /// Raise a UIA event on an element (invoke, toggle, expand, collapse, select).
    /// </summary>
    string RaiseEvent(AutomationElement element, string eventName);

    /// <summary>
    /// Listen for a UIA event on an element or process, with timeout.
    /// </summary>
    Task<(bool fired, string? eventDetails, long elapsedMs)> ListenForEventAsync(
        AutomationElement? element, string eventType, int timeoutMs = 10000);

    /// <summary>
    /// Open a context menu on an element.
    /// </summary>
    AutomationElement? OpenContextMenu(AutomationElement element);

    /// <summary>
    /// Get the current clipboard text content.
    /// </summary>
    string? GetClipboardText();

    /// <summary>
    /// Set clipboard text content.
    /// </summary>
    void SetClipboardText(string text);

    /// <summary>
    /// Read tooltip or help text from an element.
    /// </summary>
    string? GetTooltipText(AutomationElement element);

    /// <summary>
    /// Find all elements matching a condition (not just the first).
    /// </summary>
    AutomationElement[]? FindAllMatching(string? automationId = null, string? name = null,
        string? className = null, string? controlType = null, AutomationElement? parent = null, int timeoutMs = 5000);
}