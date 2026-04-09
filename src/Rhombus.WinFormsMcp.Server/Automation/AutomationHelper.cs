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
/// Helper class for WinForms UI automation using FlaUI with UIA2 backend.
/// When headless, processes are launched on a hidden desktop (CreateDesktop)
/// for complete UI isolation — no focus stealing, no visible windows.
/// </summary>
public class AutomationHelper : IAutomationHelper {
    private UIA2Automation? _automation;
    private readonly Dictionary<string, Process> _launchedProcesses = [];
    private readonly ConcurrentDictionary<int, StringBuilder> _stderrBuffers = new();
    private readonly object _lock = new object();

    /// <summary>Desktop handle for the hidden desktop (IntPtr.Zero when not headless).</summary>
    private IntPtr _hiddenDesktop;
    private const string HiddenDesktopName = "McpAutomation";

    /// <summary>Maps PID → desktop handle. Headless-launched processes get _hiddenDesktop; attached processes get IntPtr.Zero (default desktop).</summary>
    private readonly ConcurrentDictionary<int, IntPtr> _processDesktops = new();

    /// <summary>Maps PID → HWND found via EnumDesktopWindows (for hidden desktop processes where Process.MainWindowHandle is zero).</summary>
    private readonly ConcurrentDictionary<int, IntPtr> _processWindows = new();

    /// <summary>Maps PID → native process handle from CreateProcess (for exit code access).</summary>
    private readonly ConcurrentDictionary<int, IntPtr> _nativeProcessHandles = new();

    public bool Headless { get; }

    public AutomationHelper(bool headless = false) {
        Headless = headless;
        _automation = new UIA2Automation();

        if (headless) {
            _hiddenDesktop = NativeMethods.CreateHiddenDesktop(HiddenDesktopName);
            if (_hiddenDesktop == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Failed to create hidden desktop '{HiddenDesktopName}'. " +
                    "Headless mode requires the CreateDesktop Win32 API.");
        }
    }

    /// <summary>
    /// Launch a WinForms application.
    /// When headless, uses CreateProcess to launch on the hidden desktop for complete UI isolation.
    /// When not headless, uses standard Process.Start on the user's visible desktop.
    /// </summary>
    public Process LaunchApp(string path, string? arguments = null, string? workingDirectory = null) {
        Process process;

        if (Headless && _hiddenDesktop != IntPtr.Zero) {
            // Build command line: quote the path, append arguments
            var commandLine = string.IsNullOrEmpty(arguments)
                ? $"\"{path}\""
                : $"\"{path}\" {arguments}";

            var result = NativeMethods.LaunchOnDesktop(HiddenDesktopName, commandLine, workingDirectory);
            if (result.Pid < 0)
                throw new InvalidOperationException($"Failed to launch {path} on hidden desktop");

            // Get the Process object while the native handle is still open (prevents zombie cleanup race)
            process = Process.GetProcessById(result.Pid);
            // Keep native handle for exit code access (Process.ExitCode can fail for GetProcessById-obtained objects)
            _nativeProcessHandles[result.Pid] = result.ProcessHandle;
            _processDesktops[result.Pid] = _hiddenDesktop;

            // Capture stderr asynchronously (same pattern as the non-headless path)
            if (result.Stderr != null) {
                var stderrBuffer = new StringBuilder();
                _stderrBuffers[result.Pid] = stderrBuffer;
                var reader = result.Stderr;
                _ = Task.Run(async () => {
                    try {
                        while (true) {
                            var line = await reader.ReadLineAsync();
                            if (line == null) break;
                            stderrBuffer.AppendLine(line);
                        }
                    }
                    catch { }
                    finally { reader.Dispose(); }
                });
            }
        }
        else {
            var psi = new ProcessStartInfo {
                FileName = path,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch {path}");
            _processDesktops[process.Id] = IntPtr.Zero; // default desktop

            // Capture stderr asynchronously to avoid deadlocks
            var stderrBuffer = new StringBuilder();
            _stderrBuffers[process.Id] = stderrBuffer;
            process.ErrorDataReceived += (sender, e) => {
                if (e.Data != null)
                    stderrBuffer.AppendLine(e.Data);
            };
            process.BeginErrorReadLine();
        }

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
    /// Attach to a running process (on the user's visible desktop).
    /// </summary>
    public Process AttachToProcess(int pid) {
        var process = Process.GetProcessById(pid);
        _processDesktops[pid] = IntPtr.Zero; // attached processes are always on the default desktop
        lock (_lock) {
            _launchedProcesses[pid.ToString()] = process;
        }
        return process;
    }

    /// <summary>
    /// Attach to a running process by name (on the user's visible desktop).
    /// </summary>
    public Process AttachToProcessByName(string name) {
        var processes = Process.GetProcessesByName(name);
        if (processes.Length == 0)
            throw new InvalidOperationException($"No process found with name: {name}");

        var process = processes[0];
        _processDesktops[process.Id] = IntPtr.Zero;
        lock (_lock) {
            _launchedProcesses[process.Id.ToString()] = process;
        }
        return process;
    }

    /// <summary>
    /// Get main window element of a process.
    /// For hidden desktop processes, uses EnumDesktopWindows + SetThreadDesktop to find and access the window.
    /// For default desktop processes, uses Process.MainWindowHandle as before.
    /// </summary>
    public AutomationElement? GetMainWindow(int pid) {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        try {
            var desktop = GetDesktopForProcess(pid);

            if (desktop != IntPtr.Zero) {
                // Hidden desktop: find HWND via EnumDesktopWindows, access via SetThreadDesktop
                var hwnd = GetOrFindWindowHandle(pid, desktop);
                if (hwnd == IntPtr.Zero) return null;

                return NativeMethods.WithDesktop(desktop, () => _automation.FromHandle(hwnd));
            }

            // Default desktop: standard path
            var process = Process.GetProcessById(pid);
            if (process.MainWindowHandle == IntPtr.Zero) return null;
            return _automation.FromHandle(process.MainWindowHandle);
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Get or discover the HWND for a process on a hidden desktop.
    /// Caches the result for subsequent calls.
    /// </summary>
    private IntPtr GetOrFindWindowHandle(int pid, IntPtr desktop) {
        if (_processWindows.TryGetValue(pid, out var cached) && cached != IntPtr.Zero)
            return cached;

        var hwnd = NativeMethods.FindWindowOnDesktop(desktop, pid);
        if (hwnd != IntPtr.Zero)
            _processWindows[pid] = hwnd;
        return hwnd;
    }

    /// <summary>
    /// Returns the desktop handle for a tracked process, or IntPtr.Zero for the default desktop.
    /// </summary>
    private IntPtr GetDesktopForProcess(int pid) {
        return _processDesktops.TryGetValue(pid, out var desktop) ? desktop : IntPtr.Zero;
    }

    /// <summary>
    /// Returns true if the given process is running on the hidden desktop.
    /// </summary>
    private bool IsOnHiddenDesktop(int pid) {
        return GetDesktopForProcess(pid) != IntPtr.Zero;
    }

    /// <summary>
    /// Returns true if the given element belongs to a process on the hidden desktop.
    /// </summary>
    private bool IsOnHiddenDesktop(AutomationElement element) {
        try {
            var pid = element.Properties.ProcessId.ValueOrDefault;
            return pid > 0 && IsOnHiddenDesktop(pid);
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// Throws if the element is on the hidden desktop and the operation requires input simulation.
    /// </summary>
    private void ThrowIfHeadless(AutomationElement element, string operation, string? alternative = null) {
        if (!IsOnHiddenDesktop(element)) return;
        var msg = $"{operation} requires input simulation and is not available for headless processes (the target element belongs to a process on the hidden desktop).";
        if (alternative != null)
            msg += $" Use {alternative} instead.";
        throw new InvalidOperationException(msg);
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
    /// Find multiple elements matching condition.
    /// When parent is from a hidden desktop process, UIA queries work because
    /// GetMainWindow already called SetThreadDesktop before returning the element.
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
    /// Switch the calling thread to a process's desktop, execute an action, then restore.
    /// No-op for default desktop processes.
    /// </summary>
    public T OnProcessDesktop<T>(int pid, Func<T> action) {
        var desktop = GetDesktopForProcess(pid);
        if (desktop != IntPtr.Zero)
            return NativeMethods.WithDesktop(desktop, action);
        return action();
    }

    /// <summary>
    /// Non-generic overload.
    /// </summary>
    public void OnProcessDesktop(int pid, Action action) {
        var desktop = GetDesktopForProcess(pid);
        if (desktop != IntPtr.Zero)
            NativeMethods.WithDesktop(desktop, action);
        else
            action();
    }

    /// <summary>
    /// Check if element exists
    /// </summary>
    public bool ElementExists(string automationId, AutomationElement? parent = null) {
        return FindByAutomationId(automationId, parent, 1000) != null;
    }

    /// <summary>
    /// Click element using UIA InvokePattern (works on hidden desktops).
    /// Falls back to mouse simulation for double-click or when InvokePattern is unavailable.
    /// </summary>
    public void Click(AutomationElement element, bool doubleClick = false) {
        if (doubleClick) {
            // No UIA pattern for double-click; requires mouse simulation (default desktop only)
            ThrowIfHeadless(element, "Double-click", "single click_element (which uses UIA InvokePattern)");
            element.DoubleClick();
            return;
        }

        // Prefer InvokePattern — it's a direct UIA call, works across desktops
        var invokePattern = element.Patterns.Invoke.PatternOrDefault;
        if (invokePattern != null) {
            invokePattern.Invoke();
            return;
        }

        // Try TogglePattern for checkboxes/toggle buttons
        var togglePattern = element.Patterns.Toggle.PatternOrDefault;
        if (togglePattern != null) {
            togglePattern.Toggle();
            return;
        }

        // Try ExpandCollapsePattern for combo boxes/tree nodes
        var expandPattern = element.Patterns.ExpandCollapse.PatternOrDefault;
        if (expandPattern != null) {
            var state = expandPattern.ExpandCollapseState;
            if (state == FlaUI.Core.Definitions.ExpandCollapseState.Collapsed)
                expandPattern.Expand();
            else
                expandPattern.Collapse();
            return;
        }

        // Fallback: mouse simulation (only works on default desktop)
        ThrowIfHeadless(element, "click_element (no UIA pattern available for this control)", "get_property to read state, or set_value to change it");
        element.Click();
    }

    /// <summary>
    /// Type text into element using UIA ValuePattern (works on hidden desktops).
    /// Falls back to SendKeys when ValuePattern is unavailable (default desktop only).
    /// </summary>
    public void TypeText(AutomationElement element, string text, bool clearFirst = false) {
        var valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern != null && !valuePattern.IsReadOnly) {
            if (clearFirst) {
                valuePattern.SetValue(text);
            }
            else {
                var current = valuePattern.Value ?? "";
                valuePattern.SetValue(current + text);
            }
            return;
        }

        // Fallback: SendKeys (only works on default/active desktop)
        ThrowIfHeadless(element, "type_text (ValuePattern not available on this control)", "send_keys on a visible process, or set_value if the control supports ValuePattern");
        element.Focus();
        if (clearFirst) {
            System.Windows.Forms.SendKeys.SendWait("^a");
            Thread.Sleep(100);
        }
        System.Windows.Forms.SendKeys.SendWait(text);
    }

    /// <summary>
    /// Set value on element using UIA ValuePattern (works on hidden desktops).
    /// Falls back to SendKeys when ValuePattern is unavailable (default desktop only).
    /// </summary>
    public void SetValue(AutomationElement element, string value) {
        var valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern != null && !valuePattern.IsReadOnly) {
            valuePattern.SetValue(value);
            return;
        }

        // Fallback: SendKeys (only works on default/active desktop)
        ThrowIfHeadless(element, "set_value (ValuePattern not available or read-only on this control)");
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
    /// Take screenshot of element or process window.
    /// Uses PrintWindow (works on hidden desktops and off-screen windows).
    /// Falls back to FlaUI Capture for default desktop elements.
    /// </summary>
    public void TakeScreenshot(string outputPath, AutomationElement? element = null) {
        TakeScreenshot(outputPath, element, pid: null);
    }

    /// <summary>
    /// Take screenshot with optional PID for PrintWindow-based capture.
    /// When pid is provided (or element belongs to a hidden-desktop process), uses PrintWindow.
    /// </summary>
    public void TakeScreenshot(string outputPath, AutomationElement? element, int? pid) {
        try {
            Bitmap? bitmap = null;

            // Try PrintWindow path for any process we know about
            if (pid != null) {
                bitmap = CaptureProcessWindow(pid.Value);
            }

            // Try FlaUI Capture for elements on the default desktop
            if (bitmap == null && element != null) {
                try {
                    bitmap = element.Capture();
                }
                catch {
                    // FlaUI Capture failed (e.g., hidden desktop) — try PrintWindow via element's process
                    var processId = element.Properties.ProcessId.ValueOrDefault;
                    if (processId > 0)
                        bitmap = CaptureProcessWindow(processId);
                }
            }

            // Last resort: capture the whole default desktop
            if (bitmap == null && _automation != null) {
                var desktopElement = _automation.GetDesktop();
                bitmap = desktopElement.Capture();
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
    /// Capture a process's main window using PrintWindow.
    /// Works for both hidden desktop and default desktop processes.
    /// </summary>
    private Bitmap? CaptureProcessWindow(int pid) {
        var desktop = GetDesktopForProcess(pid);

        IntPtr hwnd;
        if (desktop != IntPtr.Zero) {
            hwnd = GetOrFindWindowHandle(pid, desktop);
        }
        else {
            try {
                hwnd = Process.GetProcessById(pid).MainWindowHandle;
            }
            catch {
                return null;
            }
        }

        return NativeMethods.CaptureWindow(hwnd);
    }

    /// <summary>
    /// Drag and drop using mouse simulation.
    /// NOTE: Only works on the default (visible) desktop. Not available for headless-launched processes.
    /// </summary>
    public void DragDrop(AutomationElement source, AutomationElement target) {
        ThrowIfHeadless(source, "drag_drop", "click_element and set_value sequences");
        ThrowIfHeadless(target, "drag_drop", "click_element and set_value sequences");

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
    /// Send keyboard keys using SendKeys simulation.
    /// NOTE: Only works on the default (visible) desktop. Not available for headless-launched processes.
    /// Use type_text/set_value (which use UIA ValuePattern) for text input on headless processes.
    /// </summary>
    public void SendKeys(string keys, int? targetPid = null) {
        if (targetPid != null && IsOnHiddenDesktop(targetPid.Value))
            throw new InvalidOperationException(
                "send_keys requires input simulation and is not available for headless processes " +
                "(the target process is running on the hidden desktop). " +
                "Use type_text or set_value (which use UIA ValuePattern) for text input on headless processes.");
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
                    _processDesktops.TryRemove(pid, out _);
                    _processWindows.TryRemove(pid, out _);
                    if (_nativeProcessHandles.TryRemove(pid, out var nativeHandle))
                        NativeMethods.CloseNativeHandle(nativeHandle);
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
            catch {
                // Process.ExitCode can fail for processes obtained via GetProcessById.
                // Fall back to native GetExitCodeProcess for CreateProcess-launched processes.
                if (_nativeProcessHandles.TryGetValue(pid, out var nativeHandle))
                    result["exitCode"] = NativeMethods.GetExitCode(nativeHandle);
                else
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
            _processDesktops.Clear();
            _processWindows.Clear();
            foreach (var handle in _nativeProcessHandles.Values)
                NativeMethods.CloseNativeHandle(handle);
            _nativeProcessHandles.Clear();
        }

        _automation?.Dispose();
        _automation = null;

        if (_hiddenDesktop != IntPtr.Zero) {
            NativeMethods.CloseHiddenDesktop(_hiddenDesktop);
            _hiddenDesktop = IntPtr.Zero;
        }
    }
}