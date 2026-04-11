using System;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// No-op telemetry implementation used when telemetry is opted out.
/// </summary>
class NullTelemetry : ITelemetry {
    public void TrackToolCall(string toolName, TimeSpan duration) { }
    public void TrackException(Exception ex) { }
    public void Flush() { }
}