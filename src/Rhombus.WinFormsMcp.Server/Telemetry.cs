using System;

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

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
    // TODO: Replace with a real Application Insights connection string before first release.
    private const string ConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000";

    private readonly TelemetryClient _client;

    public Telemetry() {
        var config = TelemetryConfiguration.CreateDefault();
        config.ConnectionString = ConnectionString;
        _client = new TelemetryClient(config);
    }

    public void TrackToolCall(string toolName, TimeSpan duration) {
        var pv = new PageViewTelemetry(toolName) { Duration = duration };
        _client.TrackPageView(pv);
    }

    public void TrackException(Exception ex) {
        _client.TrackException(new ExceptionTelemetry(ex));
    }

    public void Flush() {
        _client.Flush();
    }
}
