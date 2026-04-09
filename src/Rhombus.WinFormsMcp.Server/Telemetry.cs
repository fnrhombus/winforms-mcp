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

/// <summary>
/// No-op telemetry implementation used when telemetry is opted out.
/// </summary>
class NullTelemetry : ITelemetry {
    public void TrackToolCall(string toolName, TimeSpan duration) { }
    public void TrackException(Exception ex) { }
    public void Flush() { }
}

/// <summary>
/// Application Insights telemetry implementation.
/// </summary>
class Telemetry : ITelemetry {
    public Telemetry() {
        // Placeholder — Application Insights integration can be added later.
    }

    public void TrackToolCall(string toolName, TimeSpan duration) {
        // Future: send to Application Insights
    }

    public void TrackException(Exception ex) {
        // Future: send to Application Insights
    }

    public void Flush() {
        // Future: flush Application Insights buffer
    }
}
