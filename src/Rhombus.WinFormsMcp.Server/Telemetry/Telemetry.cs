using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// Application Insights telemetry implementation.
/// Each server process gets a unique session ID so User Flows can visualize
/// tool-chaining patterns. An operation sequence counter orders events within
/// a session.
/// </summary>
class Telemetry : ITelemetry {
    // TODO: Replace with a real Application Insights connection string.
    // With a placeholder key, App Insights silently drops all events.
    private const string ConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000";

    private readonly TelemetryClient _client;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private int _operationSequence;

    public Telemetry() {
        var config = TelemetryConfiguration.CreateDefault();
        config.ConnectionString = ConnectionString;
        config.TelemetryInitializers.Add(new StripPiiInitializer(_sessionId));
        _client = new TelemetryClient(config);
        _client.Context.Session.Id = _sessionId;
    }

    public void TrackToolCall(string toolName, TimeSpan duration) {
        var seq = Interlocked.Increment(ref _operationSequence);
        var pv = new PageViewTelemetry(toolName) { Duration = duration };
        pv.Properties["sequence"] = seq.ToString();
        _client.TrackPageView(pv);
    }

    public void TrackException(Exception ex) {
        _client.TrackException(new ExceptionTelemetry(ex));
    }

    public void Flush() {
        _client.Flush();
    }

    /// <summary>
    /// Strips or hashes PII fields before telemetry leaves the process.
    /// Machine name is hashed; user/account IDs are replaced with the session ID
    /// so events correlate within a session but can't identify a person.
    /// </summary>
    private class StripPiiInitializer : ITelemetryInitializer {
        private readonly string _sessionId;
        private readonly string _hashedMachine;

        public StripPiiInitializer(string sessionId) {
            _sessionId = sessionId;
            _hashedMachine = HashString(System.Environment.MachineName);
        }

        public void Initialize(Microsoft.ApplicationInsights.Channel.ITelemetry telemetry) {
            telemetry.Context.User.Id = _sessionId;
            telemetry.Context.User.AccountId = null;
            telemetry.Context.Device.Id = _hashedMachine;
            telemetry.Context.Cloud.RoleInstance = _hashedMachine;
        }

        private static string HashString(string input) {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
        }
    }
}
