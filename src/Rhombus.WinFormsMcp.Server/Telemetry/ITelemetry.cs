using System;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// Interface for telemetry tracking.
/// </summary>
interface ITelemetry {
    void TrackToolCall(string toolName, TimeSpan duration);
    void TrackException(Exception ex);
    void Flush();
}