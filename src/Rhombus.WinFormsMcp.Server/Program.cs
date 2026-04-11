using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FlaUI.Core.AutomationElements;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rhombus.WinFormsMcp.Rendering;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// WinFormsMcp - MCP Server for WinForms Automation
///
/// This server provides tools for automating WinForms applications in a headless manner.
/// It communicates via JSON-RPC over stdio (compatible with Claude Code).
/// </summary>
class Program {
    static async Task Main(string[] args) {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) => {
                services.AddSingleton<IPostConfigureOptions<McpServerOptions>>(
                    new McpServerOptionsConfiguration(context.Configuration));
                services.Configure<McpServerOptions>(context.Configuration);

                services.AddMemoryCache();
                services.AddSingleton<IAutomationHelper>(sp => {
                    var opts = sp.GetRequiredService<IOptions<McpServerOptions>>();
                    return new AutomationHelper(opts.Value.Headless, sp.GetRequiredService<ILogger<AutomationHelper>>());
                });
                services.AddSingleton<ISessionManager, SessionManager>();
                services.AddSingleton<RendererProcessPool>();

                services.AddSingleton(sp => {
                    var opts = sp.GetRequiredService<IOptions<McpServerOptions>>();
                    return opts.Value.TelemetryOptOut
                        ? (ITelemetry)sp.GetRequiredService<NullTelemetry>()
                        : sp.GetRequiredService<Telemetry>();
                });
                services.AddSingleton<NullTelemetry>();
                services.AddSingleton<Telemetry>();

                services.AddHostedService<AutomationServer>();
            })
            .ConfigureLogging((context, logging) => {
                logging.ClearProviders();

                var options = new McpServerOptions();
                var optionsConfig = new McpServerOptionsConfiguration(context.Configuration);
                optionsConfig.PostConfigure(Options.DefaultName, options);

                logging.SetMinimumLevel(options.MinimumLogLevel);
                logging.AddConsole(consoleOptions => {
                    consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace;
                });
            })
            .Build();

        await host.RunAsync();
    }
}
