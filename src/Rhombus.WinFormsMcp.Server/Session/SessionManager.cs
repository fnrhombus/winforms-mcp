using System;
using System.Collections.Generic;

using FlaUI.Core.AutomationElements;

using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// Session manager for tracking automation contexts and element references
/// </summary>
class SessionManager : ISessionManager {
    private readonly Dictionary<string, AutomationElement> _elementCache = new();
    private readonly Dictionary<int, object> _processContext = new();
    private int _nextElementId = 1;
    private readonly IAutomationHelper _automation;

    public SessionManager(IAutomationHelper automation) {
        _automation = automation;
    }

    public IAutomationHelper GetAutomation() {
        return _automation;
    }

    public string CacheElement(AutomationElement element) {
        var id = $"elem_{_nextElementId++}";
        _elementCache[id] = element;
        return id;
    }

    public AutomationElement? GetElement(string elementId) {
        return _elementCache.TryGetValue(elementId, out var elem) ? elem : null;
    }

    public void ClearElement(string elementId) {
        _elementCache.Remove(elementId);
    }

    public void CacheProcess(int pid, object context) {
        _processContext[pid] = context;
    }

    public void Dispose() {
        _automation?.Dispose();
    }
}