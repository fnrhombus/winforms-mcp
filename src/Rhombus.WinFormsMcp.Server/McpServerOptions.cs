namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// Strongly-typed configuration for the MCP server, bound from environment variables.
/// </summary>
public class McpServerOptions {
    public bool Headless { get; set; }
    public bool TelemetryOptOut { get; set; }
    public string Tfm { get; set; } = "auto";
}
