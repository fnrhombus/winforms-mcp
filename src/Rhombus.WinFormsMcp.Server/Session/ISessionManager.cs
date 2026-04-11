using System;

using FlaUI.Core.AutomationElements;

using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// Interface for session management — tracking automation contexts and element references.
/// </summary>
interface ISessionManager : IDisposable {
    IAutomationHelper GetAutomation();
    string CacheElement(AutomationElement element);
    AutomationElement? GetElement(string elementId);
    void ClearElement(string elementId);
    void CacheProcess(int pid, object context);
}
