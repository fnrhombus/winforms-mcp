using System;
using System.Diagnostics;

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// Lightweight Application Insights telemetry wrapper.
/// Disabled entirely when TELEMETRY_OPTOUT=true or TELEMETRY_OPTOUT=1.
/// </summary>
static class Telemetry {
    // TODO: Replace with a real Application Insights connection string before first release.
    private const string ConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000";

    private static readonly TelemetryClient? _client;

    static Telemetry() {
        var optOut = Environment.GetEnvironmentVariable("TELEMETRY_OPTOUT");
        if (string.Equals(optOut, "true", StringComparison.OrdinalIgnoreCase) || optOut == "1")
            return;

        var config = TelemetryConfiguration.CreateDefault();
        config.ConnectionString = ConnectionString;
        _client = new TelemetryClient(config);
    }

    public static void TrackToolCall(string toolName, TimeSpan duration) {
        if (_client is null) return;
        var pv = new PageViewTelemetry(toolName) { Duration = duration };
        _client.TrackPageView(pv);
    }

    public static void TrackException(Exception ex) {
        _client?.TrackException(new ExceptionTelemetry(ex));
    }

    public static void Flush() {
        _client?.Flush();
    }
}
