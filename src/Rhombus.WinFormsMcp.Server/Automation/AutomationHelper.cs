using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA2;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Helper class for WinForms UI automation using FlaUI with UIA2 backend
/// </summary>
public class AutomationHelper : IAutomationHelper {
    private UIA2Automation? _automation;
    private readonly Dictionary<string, Process> _launchedProcesses = [];
    private readonly ConcurrentDictionary<int, StringBuilder> _stderrBuffers = new();
    private readonly object _lock = new object();

    public bool Headless { get; }

    public AutomationHelper(bool headless = false) {
        Headless = headless;
        _automation = new UIA2Automation();
    }

    /// <summary>
    /// Launch a WinForms application
    /// </summary>
    public Process LaunchApp(string path, string? arguments = null, string? workingDirectory = null) {
        var psi = new ProcessStartInfo {
            FileName = path,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = Headless,
            RedirectStandardError = true,
            WindowStyle = Headless ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch {path}");

        // Capture stderr asynchronously to avoid deadlocks
        var stderrBuffer = new StringBuilder();
        _stderrBuffers[process.Id] = stderrBuffer;
        process.ErrorDataReceived += (sender, e) => {
            if (e.Data != null)
                stderrBuffer.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();

        try {
            process.WaitForInputIdle(5000);
        }
        catch (InvalidOperationException) // COVERAGE_EXCEPTION: Console apps without GUI throw here
        {
            // Process does not have a graphical interface (e.g., console app)
        }

        lock (_lock) {
            _launchedProcesses[process.Id.ToString()] = process;
        }

        return process;
    }

    /// <summary>
    /// Attach to a running process
    /// </summary>
    public Process AttachToProcess(int pid) {
        var process = Process.GetProcessById(pid);
        lock (_lock) {
            _launchedProcesses[pid.ToString()] = process;
        }
        return process;
    }

    /// <summary>
    /// Attach to a running process by name
    /// </summary>
    public Process AttachToProcessByName(string name) {
        var processes = Process.GetProcessesByName(name);
        if (processes.Length == 0)
            throw new InvalidOperationException($"No process found with name: {name}");

        var process = processes[0];
        lock (_lock) {
            _launchedProcesses[process.Id.ToString()] = process;
        }
        return process;
    }

    /// <summary>
    /// Get main window element of a process
    /// </summary>
    public AutomationElement? GetMainWindow(int pid) {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        try {
            var process = Process.GetProcessById(pid);
            return _automation.FromHandle(process.MainWindowHandle);
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Find element by AutomationId
    /// </summary>
    public AutomationElement? FindByAutomationId(string automationId, AutomationElement? parent = null, int timeoutMs = 5000) {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var condition = new PropertyCondition(_automation.PropertyLibrary.Element.AutomationId, automationId);
        return FindElement(condition, parent, timeoutMs);
    }

    /// <summary>
    /// Find element by Name
    /// </summary>
    public AutomationElement? FindByName(string name, AutomationElement? parent = null, int timeoutMs = 5000) {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var condition = new PropertyCondition(_automation.PropertyLibrary.Element.Name, name);
        return FindElement(condition, parent, timeoutMs);
    }

    /// <summary>
    /// Find element by ClassName
    /// </summary>
    public AutomationElement? FindByClassName(string className, AutomationElement? parent = null, int timeoutMs = 5000) {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var condition = new PropertyCondition(_automation.PropertyLibrary.Element.ClassName, className);
        return FindElement(condition, parent, timeoutMs);
    }

    /// <summary>
    /// Find element by ControlType
    /// </summary>
    public AutomationElement? FindByControlType(ControlType controlType, AutomationElement? parent = null, int timeoutMs = 5000) {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var condition = new PropertyCondition(_automation.PropertyLibrary.Element.ControlType, controlType);
        return FindElement(condition, parent, timeoutMs);
    }

    /// <summary>
    /// Find multiple elements matching condition
    /// </summary>
    public AutomationElement[]? FindAll(ConditionBase condition, AutomationElement? parent = null, int timeoutMs = 5000) {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var root = parent ?? _automation.GetDesktop();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs) {
            try {
                var elements = root.FindAllChildren(condition);
                if (elements.Length > 0)
                    return elements;
            }
            catch { }

            Thread.Sleep(100);
        }

        return null;
    }

    /// <summary>
    /// Find element with retry/timeout
    /// </summary>
    private AutomationElement? FindElement(ConditionBase condition, AutomationElement? parent, int timeoutMs) {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var root = parent ?? _automation.GetDesktop();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs) {
            try {
                var element = root.FindFirstChild(condition);
                if (element != null)
                    return element;
            }
            catch { }

            Thread.Sleep(100);
        }

        return null;
    }

    /// <summary>
    /// Check if element exists
    /// </summary>
    public bool ElementExists(string automationId, AutomationElement? parent = null) {
        return FindByAutomationId(automationId, parent, 1000) != null;
    }

    /// <summary>
    /// Click element
    /// </summary>
    public void Click(AutomationElement element, bool doubleClick = false) {
        if (doubleClick) {
            element.DoubleClick();
        }
        else {
            element.Click();
        }
    }

    /// <summary>
    /// Type text into element
    /// </summary>
    public void TypeText(AutomationElement element, string text, bool clearFirst = false) {
        element.Focus();

        if (clearFirst) {
            System.Windows.Forms.SendKeys.SendWait("^a");
            Thread.Sleep(100);
        }

        System.Windows.Forms.SendKeys.SendWait(text);
    }

    /// <summary>
    /// Set value on element
    /// </summary>
    public void SetValue(AutomationElement element, string value) {
        element.Focus();
        System.Windows.Forms.SendKeys.SendWait("^a");
        Thread.Sleep(50);
        System.Windows.Forms.SendKeys.SendWait(value);
    }

    /// <summary>
    /// Get element property, including UIA pattern-based values.
    /// Supported properties: name, automationId, className, controlType, isOffscreen, isEnabled,
    /// value, text, isChecked, toggleState, isSelected, selectedItem, items, itemCount,
    /// boundingRectangle, isExpanded, min, max, current.
    /// </summary>
    public object? GetProperty(AutomationElement element, string propertyName) {
        return propertyName.ToLower() switch {
            "name" => element.Name,
            "automationid" => element.AutomationId,
            "classname" => element.ClassName,
            "controltype" => element.ControlType.ToString(),
            "isoffscreen" => element.IsOffscreen,
            "isenabled" => element.IsEnabled,

            // ValuePattern: value / text
            "value" or "text" => GetValuePatternValue(element),

            // TogglePattern: isChecked / toggleState
            "ischecked" or "togglestate" => GetTogglePatternState(element),

            // SelectionItemPattern: isSelected
            "isselected" => GetSelectionItemPatternIsSelected(element),

            // SelectionPattern: selectedItem
            "selecteditem" => GetSelectionPatternSelectedItem(element),

            // Child items: items, itemCount
            "items" => GetChildItemNames(element),
            "itemcount" => GetChildItemCount(element),

            // BoundingRectangle
            "boundingrectangle" => GetBoundingRectangleJson(element),

            // ExpandCollapsePattern: isExpanded
            "isexpanded" => GetExpandCollapseState(element),

            // RangeValuePattern: min, max, current
            "min" => GetRangeValueMin(element),
            "max" => GetRangeValueMax(element),
            "current" => GetRangeValueCurrent(element),

            _ => null
        };
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetValuePatternValue(AutomationElement element) {
        var pattern = element.Patterns.Value;
        if (pattern.IsSupported)
            return pattern.Pattern.Value.ValueOrDefault;

        // Fall back to element Name when ValuePattern is not supported
        return element.Name;
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetTogglePatternState(AutomationElement element) {
        var pattern = element.Patterns.Toggle;
        if (pattern.IsSupported)
            return pattern.Pattern.ToggleState.ValueOrDefault.ToString();

        return "TogglePattern not supported on this element";
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetSelectionItemPatternIsSelected(AutomationElement element) {
        var pattern = element.Patterns.SelectionItem;
        if (pattern.IsSupported)
            return pattern.Pattern.IsSelected.ValueOrDefault;

        return "SelectionItemPattern not supported on this element";
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetSelectionPatternSelectedItem(AutomationElement element) {
        var pattern = element.Patterns.Selection;
        if (pattern.IsSupported) {
            var selection = pattern.Pattern.Selection.ValueOrDefault;
            if (selection != null && selection.Length > 0)
                return selection[0].Name;
            return null;
        }

        return "SelectionPattern not supported on this element";
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetChildItemNames(AutomationElement element) {
        var children = element.FindAllChildren();
        var names = children.Select(c => c.Name ?? "").ToArray();
        return JsonSerializer.Serialize(names);
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetChildItemCount(AutomationElement element) {
        var children = element.FindAllChildren();
        return children.Length;
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetBoundingRectangleJson(AutomationElement element) {
        var rect = element.BoundingRectangle;
        return JsonSerializer.Serialize(new {
            x = rect.X,
            y = rect.Y,
            width = rect.Width,
            height = rect.Height
        });
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetExpandCollapseState(AutomationElement element) {
        var pattern = element.Patterns.ExpandCollapse;
        if (pattern.IsSupported)
            return pattern.Pattern.ExpandCollapseState.ValueOrDefault.ToString();

        return "ExpandCollapsePattern not supported on this element";
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetRangeValueMin(AutomationElement element) {
        var pattern = element.Patterns.RangeValue;
        if (pattern.IsSupported)
            return pattern.Pattern.Minimum.ValueOrDefault;

        return "RangeValuePattern not supported on this element";
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetRangeValueMax(AutomationElement element) {
        var pattern = element.Patterns.RangeValue;
        if (pattern.IsSupported)
            return pattern.Pattern.Maximum.ValueOrDefault;

        return "RangeValuePattern not supported on this element";
    }

    // COVERAGE_EXCEPTION: Pattern-based methods require live UIA elements which cannot be created in unit tests
    private static object? GetRangeValueCurrent(AutomationElement element) {
        var pattern = element.Patterns.RangeValue;
        if (pattern.IsSupported)
            return pattern.Pattern.Value.ValueOrDefault;

        return "RangeValuePattern not supported on this element";
    }

    /// <summary>
    /// Take screenshot of element or full desktop
    /// </summary>
    public void TakeScreenshot(string outputPath, AutomationElement? element = null) {
        try {
            Bitmap? bitmap = null;

            if (element != null) {
                bitmap = element.Capture();
            }
            else if (_automation != null) {
                var desktop = _automation.GetDesktop();
                bitmap = desktop.Capture();
            }

            if (bitmap != null) {
                bitmap.Save(outputPath, ImageFormat.Png);
                bitmap.Dispose();
            }
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Failed to take screenshot: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Drag and drop
    /// </summary>
    public void DragDrop(AutomationElement source, AutomationElement target) {
        var sourceBounds = source.BoundingRectangle;
        var targetBounds = target.BoundingRectangle;

        if (sourceBounds.Width == 0 || targetBounds.Width == 0)
            throw new InvalidOperationException("Source or target element has invalid bounding rectangle");

        // Simulate drag-drop using mouse movements
        var sourceCenter = new Point(
            (int)(sourceBounds.X + sourceBounds.Width / 2),
            (int)(sourceBounds.Y + sourceBounds.Height / 2)
        );

        var targetCenter = new Point(
            (int)(targetBounds.X + targetBounds.Width / 2),
            (int)(targetBounds.Y + targetBounds.Height / 2)
        );

        source.Focus();
        System.Windows.Forms.Cursor.Position = sourceCenter;
        Thread.Sleep(100);

        // Simulate mouse down, move, mouse up
        System.Windows.Forms.SendKeys.SendWait("{LDown}");
        System.Windows.Forms.Cursor.Position = targetCenter;
        Thread.Sleep(200);
        System.Windows.Forms.SendKeys.SendWait("{LUp}");
    }

    /// <summary>
    /// Send keyboard keys
    /// </summary>
    public void SendKeys(string keys) {
        System.Windows.Forms.SendKeys.SendWait(keys);
    }

    /// <summary>
    /// Close application
    /// </summary>
    public void CloseApp(int pid, bool force = false) {
        lock (_lock) {
            if (_launchedProcesses.TryGetValue(pid.ToString(), out var process)) {
                try {
                    if (force) {
                        process.Kill();
                    }
                    else {
                        process.CloseMainWindow();
                        process.WaitForExit(5000);
                        if (!process.HasExited)
                            process.Kill();
                    }
                }
                catch { }
                finally {
                    _launchedProcesses.Remove(pid.ToString());
                    _stderrBuffers.TryRemove(pid, out _);
                }
            }
        }
    }

    /// <summary>
    /// Wait for element to appear
    /// </summary>
    public async Task<bool> WaitForElementAsync(string automationId, AutomationElement? parent = null, int timeoutMs = 10000) {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs) {
            if (FindByAutomationId(automationId, parent, 500) != null)
                return true;

            await Task.Delay(100);
        }

        return false;
    }

    /// <summary>
    /// Get all child elements
    /// </summary>
    public AutomationElement[]? GetAllChildren(AutomationElement element) {
        try {
            return element.FindAllChildren();
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Build a tree of UI automation elements starting from a root element.
    /// </summary>
    public List<Dictionary<string, object?>> GetElementTree(AutomationElement root, int depth = 3, int maxElements = 50, Func<AutomationElement, string>? cacheElement = null) {
        var result = new List<Dictionary<string, object?>>();
        int elementCount = 0;
        BuildElementTree(root, depth, maxElements, cacheElement, result, ref elementCount);
        return result;
    }

    private void BuildElementTree(AutomationElement parent, int remainingDepth, int maxElements, Func<AutomationElement, string>? cacheElement, List<Dictionary<string, object?>> targetList, ref int elementCount) {
        if (remainingDepth <= 0 || elementCount >= maxElements)
            return;

        var children = GetAllChildren(parent);
        if (children == null)
            return;

        foreach (var child in children) {
            if (elementCount >= maxElements)
                break;

            elementCount++;

            var node = new Dictionary<string, object?> {
                ["name"] = TryGetElementProperty(() => child.Name),
                ["controlType"] = TryGetElementProperty(() => child.ControlType.ToString()),
                ["automationId"] = TryGetElementProperty(() => child.AutomationId),
                ["isEnabled"] = TryGetElementProperty(() => (object)child.IsEnabled),
                ["isOffscreen"] = TryGetElementProperty(() => (object)child.IsOffscreen),
            };

            try {
                var rect = child.BoundingRectangle;
                node["boundingRectangle"] = new Dictionary<string, object> {
                    ["x"] = rect.X,
                    ["y"] = rect.Y,
                    ["width"] = rect.Width,
                    ["height"] = rect.Height
                };
            }
            catch {
                node["boundingRectangle"] = null;
            }

            if (cacheElement != null) {
                node["elementId"] = cacheElement(child);
            }

            var childList = new List<Dictionary<string, object?>>();
            if (remainingDepth > 1 && elementCount < maxElements) {
                BuildElementTree(child, remainingDepth - 1, maxElements, cacheElement, childList, ref elementCount);
            }
            node["children"] = childList;

            targetList.Add(node);
        }
    }

    /// <summary>
    /// Get the status of a process including whether it is running, responding, exit code, and captured stderr.
    /// </summary>
    public Dictionary<string, object?> GetProcessStatus(int pid) {
        var result = new Dictionary<string, object?>();

        Process? process = null;
        lock (_lock) {
            _launchedProcesses.TryGetValue(pid.ToString(), out process);
        }

        // If not in our tracked processes, try to get it from the system
        process ??= TryGetProcessById(pid);

        if (process == null) {
            result["isRunning"] = false;
            result["hasExited"] = true;
            result["exitCode"] = null;
            result["responding"] = false;
            result["mainWindowTitle"] = "";
            result["stderr"] = GetStderr(pid);
            return result;
        }

        bool hasExited;
        try {
            hasExited = process.HasExited;
        }
        catch // COVERAGE_EXCEPTION: Process access can throw if handle is invalid
        {
            hasExited = true;
        }

        result["isRunning"] = !hasExited;
        result["hasExited"] = hasExited;

        if (hasExited) {
            try {
                result["exitCode"] = process.ExitCode;
            }
            catch // COVERAGE_EXCEPTION: ExitCode can throw if process handle is invalid
            {
                result["exitCode"] = null;
            }
            result["responding"] = false;
            result["mainWindowTitle"] = "";
        }
        else {
            result["exitCode"] = null;
            try {
                result["responding"] = process.Responding;
            }
            catch // COVERAGE_EXCEPTION: Responding can throw for processes without a UI
            {
                result["responding"] = false;
            }
            try {
                result["mainWindowTitle"] = process.MainWindowTitle ?? "";
            }
            catch // COVERAGE_EXCEPTION: MainWindowTitle can throw for some processes
            {
                result["mainWindowTitle"] = "";
            }
        }

        result["stderr"] = GetStderr(pid);
        return result;
    }

    /// <summary>
    /// Get captured stderr output for a process.
    /// </summary>
    internal string GetStderr(int pid) {
        return _stderrBuffers.TryGetValue(pid, out var buffer) ? buffer.ToString() : "";
    }

    /// <summary>
    /// Select an item in a combo box, list box, or similar selection control.
    /// Handles the expand-find-select pattern automatically.
    /// </summary>
    public string SelectItem(AutomationElement element, string? value = null, int? index = null) {
        if (value == null && index == null)
            throw new ArgumentException("Either value or index must be provided");

        // COVERAGE_EXCEPTION: FlaUI pattern interactions require real UI controls
        try {
            // Try to expand the control first (for combo boxes)
            var expandPattern = element.Patterns.ExpandCollapse.PatternOrDefault;
            if (expandPattern != null) {
                expandPattern.Expand();
                Thread.Sleep(200); // Wait for dropdown to appear
            }

            // Get child items
            var children = element.FindAllChildren();
            if (children == null || children.Length == 0)
                throw new InvalidOperationException("No items found in the selection control");

            AutomationElement? targetItem = null;

            if (value != null) {
                // Find by text value
                foreach (var child in children) {
                    try {
                        if (string.Equals(child.Name, value, StringComparison.OrdinalIgnoreCase)) {
                            targetItem = child;
                            break;
                        }
                    }
                    catch { }
                }

                if (targetItem == null)
                    throw new InvalidOperationException($"Item with value '{value}' not found in the selection control");
            }
            else if (index != null) {
                if (index.Value < 0 || index.Value >= children.Length)
                    throw new ArgumentOutOfRangeException(nameof(index), $"Index {index.Value} is out of range. Control has {children.Length} items.");

                targetItem = children[index.Value];
            }

            // Try SelectionItemPattern first
            var selectionPattern = targetItem!.Patterns.SelectionItem.PatternOrDefault;
            if (selectionPattern != null) {
                selectionPattern.Select();
            }
            else {
                // Fall back to scrolling into view and clicking
                var scrollPattern = targetItem.Patterns.ScrollItem.PatternOrDefault;
                if (scrollPattern != null) {
                    scrollPattern.ScrollIntoView();
                    Thread.Sleep(100);
                }
                targetItem.Click();
            }

            // Collapse if we expanded
            if (expandPattern != null) {
                try { expandPattern.Collapse(); }
                catch { }
            }

            return targetItem.Name ?? "";
        }
        catch (ArgumentException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) {
            throw new InvalidOperationException($"Failed to select item: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Navigate and click a menu item in a menu bar or context menu.
    /// </summary>
    public void ClickMenuItem(string[] menuPath, int? pid = null) {
        if (menuPath == null || menuPath.Length == 0)
            throw new ArgumentException("menuPath must contain at least one menu item name");

        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        // COVERAGE_EXCEPTION: FlaUI menu interactions require real UI with menu controls
        AutomationElement? searchRoot;
        if (pid != null) {
            searchRoot = GetMainWindow(pid.Value);
            if (searchRoot == null)
                throw new InvalidOperationException($"Could not find main window for process {pid.Value}");
        }
        else {
            searchRoot = _automation.GetDesktop();
        }

        AutomationElement? currentParent = searchRoot;

        for (int i = 0; i < menuPath.Length; i++) {
            var menuItemName = menuPath[i];
            AutomationElement? menuItem = null;

            // Search for menu item by name
            var condition = new PropertyCondition(_automation.PropertyLibrary.Element.Name, menuItemName);
            var stopwatch = Stopwatch.StartNew();
            var timeoutMs = 5000;

            while (stopwatch.ElapsedMilliseconds < timeoutMs) {
                try {
                    menuItem = currentParent!.FindFirstDescendant(condition);
                    if (menuItem != null)
                        break;
                }
                catch { }

                Thread.Sleep(100);
            }

            if (menuItem == null)
                throw new InvalidOperationException($"Menu item '{menuItemName}' not found at level {i} of path [{string.Join(" > ", menuPath)}]");

            if (i < menuPath.Length - 1) {
                // Not the final item - expand/click to show submenu
                var expandPattern = menuItem.Patterns.ExpandCollapse.PatternOrDefault;
                if (expandPattern != null) {
                    expandPattern.Expand();
                }
                else {
                    menuItem.Click();
                }
                Thread.Sleep(200); // Wait for submenu to appear
                currentParent = menuItem;
            }
            else {
                // Final item - invoke or click it
                var invokePattern = menuItem.Patterns.Invoke.PatternOrDefault;
                if (invokePattern != null) {
                    invokePattern.Invoke();
                }
                else {
                    menuItem.Click();
                }
            }
        }
    }

    private static Process? TryGetProcessById(int pid) {
        try {
            return Process.GetProcessById(pid);
        }
        catch {
            return null;
        }
    }

    private static object? TryGetElementProperty(Func<object?> getter) {
        try {
            return getter();
        }
        catch {
            return null;
        }
    }

    public void Dispose() {
        lock (_lock) {
            foreach (var process in _launchedProcesses.Values) {
                try {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch { }
            }

            _launchedProcesses.Clear();
            _stderrBuffers.Clear();
        }

        _automation?.Dispose();
        _automation = null;
    }
}