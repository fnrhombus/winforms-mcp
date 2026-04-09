using Microsoft.Extensions.Configuration;
using Rhombus.WinFormsMcp.Server;

namespace Rhombus.WinFormsMcp.Tests;

[TestFixture]
public class McpServerOptionsTests {
    private static McpServerOptions Bind(Dictionary<string, string?> values) {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return Program.BindOptions(new McpServerOptions(), config);
    }

    [Test]
    public void BindOptions_Defaults() {
        var opts = Bind(new Dictionary<string, string?>());
        Assert.That(opts.Headless, Is.False);
        Assert.That(opts.TelemetryOptOut, Is.False);
        Assert.That(opts.Tfm, Is.EqualTo("auto"));
    }

    [Test]
    public void BindOptions_HeadlessTrue() {
        var opts = Bind(new Dictionary<string, string?> { ["HEADLESS"] = "true" });
        Assert.That(opts.Headless, Is.True);
    }

    [Test]
    public void BindOptions_HeadlessOne() {
        var opts = Bind(new Dictionary<string, string?> { ["HEADLESS"] = "1" });
        Assert.That(opts.Headless, Is.True);
    }

    [Test]
    public void BindOptions_HeadlessCaseInsensitive() {
        var opts = Bind(new Dictionary<string, string?> { ["HEADLESS"] = "TRUE" });
        Assert.That(opts.Headless, Is.True);
    }

    [Test]
    public void BindOptions_TelemetryOptOut() {
        var opts = Bind(new Dictionary<string, string?> { ["TELEMETRY_OPTOUT"] = "true" });
        Assert.That(opts.TelemetryOptOut, Is.True);
    }

    [Test]
    public void BindOptions_TelemetryOptOutOne() {
        var opts = Bind(new Dictionary<string, string?> { ["TELEMETRY_OPTOUT"] = "1" });
        Assert.That(opts.TelemetryOptOut, Is.True);
    }

    [Test]
    public void BindOptions_TfmValue() {
        var opts = Bind(new Dictionary<string, string?> { ["TFM"] = "net48" });
        Assert.That(opts.Tfm, Is.EqualTo("net48"));
    }

    [Test]
    public void BindOptions_TfmWhitespace_DefaultsToAuto() {
        var opts = Bind(new Dictionary<string, string?> { ["TFM"] = "  " });
        Assert.That(opts.Tfm, Is.EqualTo("auto"));
    }

    [Test]
    public void BindOptions_TfmNull_DefaultsToAuto() {
        var opts = Bind(new Dictionary<string, string?> { ["TFM"] = null });
        Assert.That(opts.Tfm, Is.EqualTo("auto"));
    }

    [Test]
    public void BindOptions_AllSet() {
        var opts = Bind(new Dictionary<string, string?> {
            ["HEADLESS"] = "1",
            ["TELEMETRY_OPTOUT"] = "true",
            ["TFM"] = "net8.0-windows"
        });
        Assert.That(opts.Headless, Is.True);
        Assert.That(opts.TelemetryOptOut, Is.True);
        Assert.That(opts.Tfm, Is.EqualTo("net8.0-windows"));
    }
}
