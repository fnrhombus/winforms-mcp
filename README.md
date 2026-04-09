# Rhombus.WinFormsMcp

### The ultimate WinForms toolkit for AI agents

[![CI Status](https://github.com/rhom6us/winforms-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/rhom6us/winforms-mcp/actions/workflows/ci.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Rhombus.WinFormsMcp)](https://www.nuget.org/packages/Rhombus.WinFormsMcp)
[![NPM Version](https://img.shields.io/npm/v/@rhom6us/winforms-mcp)](https://www.npmjs.com/package/@rhom6us/winforms-mcp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

An [MCP server](https://modelcontextprotocol.io) that gives Claude (and any MCP-compatible agent) full control over Windows Forms applications — from launching and clicking buttons to previewing form layouts in real time.

- **See and operate running apps** — launch processes, find elements, click, type, drag-drop, and take screenshots, all through FlaUI/UIA2 automation
- **Design-time form preview** — renders `.Designer.cs` files to pixel-accurate PNGs *without building the project*, the same way Visual Studio's WYSIWYG designer works
- **Every framework, every version** — .NET Framework 4.x, .NET Core 3.x, .NET 5–9+. Out-of-process rendering automatically matches your project's target framework
- **Headless mode** — launch apps on a hidden desktop with zero focus stealing. Built for CI/CD, remote machines, and background agents
- **Zero configuration** — one line in your MCP config and you're running. No per-project setup, no framework selection, no build step required

## Getting Started

Add to your Claude Code MCP config (`~/.claude/mcp.json` on Windows):

**npx (recommended — nothing to install):**

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "npx",
      "args": ["-y", "@rhom6us/winforms-mcp"]
    }
  }
}
```

**dotnet tool:**

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/Rhombus.WinFormsMcp.Server.csproj"]
    }
  }
}
```

That's it. Claude can now see and interact with any WinForms application on your machine.

## What Can It Do?

### Automate running applications

Launch an app, find UI elements, interact with them, and verify results — all without touching a mouse.

```
launch_app  →  find_element  →  click_element  →  take_screenshot
```

Claude sees screenshots as inline images, so it can visually verify what it's doing and course-correct in real time.

### Preview form designs instantly

The `render_form` tool turns any `.Designer.cs` file into a PNG preview. No build required — Claude can iterate on form layouts the same way you'd use the Visual Studio designer, but entirely through code.

This works by running your designer code on a real `DesignSurface` (the same infrastructure Visual Studio uses), in an out-of-process host that matches your project's target framework. The result is a pixel-accurate rendering of exactly what Visual Studio would show.

### Full tool list

| Category | Tools |
|----------|-------|
| **Process** | `launch_app`, `attach_to_process`, `close_app`, `get_process_status` |
| **Discovery** | `find_element`, `element_exists`, `wait_for_element`, `get_element_tree` |
| **Interaction** | `click_element`, `type_text`, `set_value`, `select_item`, `click_menu_item`, `drag_drop`, `send_keys` |
| **Visual** | `take_screenshot`, `render_form` |

## Cross-Framework Rendering

`render_form` automatically detects your project's target framework from its `.csproj` and dispatches rendering to a matching out-of-process host:

| Your project targets | Renderer host used |
|---|---|
| .NET Framework 4.0–4.8.x | `net48` |
| .NET Core 3.x | `netcoreapp3.1` |
| .NET 5, 6, 7, 8, 9+ | `net8.0-windows` |

This means custom controls, third-party components, and framework-specific APIs all resolve correctly — no manual configuration needed.

Override the auto-detection with the `TFM` environment variable if needed:

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "npx",
      "args": ["-y", "@rhom6us/winforms-mcp"],
      "env": { "TFM": "net48" }
    }
  }
}
```

## Headless Mode

When `HEADLESS=true`, launched applications run on a hidden Windows desktop (`CreateDesktop` API) with complete UI isolation:

- **Zero focus stealing** — the app cannot steal focus, show TopMost windows, or flash in the taskbar, even if it calls `this.Activate()` or `SetForegroundWindow`
- **Invisible to the user** — the hidden desktop is entirely separate from the user's visible desktop
- **Full automation support** — element discovery, clicking, typing, and screenshots all work through UIA patterns and PrintWindow
- **Mixed mode** — attach to visible apps and launch headless apps in the same session. Each process is automatically routed to its correct desktop.

Enable it in your MCP config:

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "npx",
      "args": ["-y", "@rhom6us/winforms-mcp"],
      "env": { "HEADLESS": "true" }
    }
  }
}
```

**Limitations:** `send_keys` and `drag_drop` require input simulation and only work on the visible desktop (i.e., with `attach_to_process`). For headless processes, use `type_text`/`set_value` (which use UIA ValuePattern) and `click_element` (which uses UIA InvokePattern) instead.

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `HEADLESS` | `false` | Launch applications on a hidden desktop for zero-disruption automation. Set to `true` to enable. |
| `TFM` | `auto` | Lock rendering to a specific framework (`net48`, `netcoreapp3.1`, `net8.0-windows`), or `auto` to detect from the project. |

## Documentation

For detailed setup instructions, tool reference, examples, and troubleshooting, see the [docs](docs/) folder:

- [Claude Code Setup Guide](docs/CLAUDE_CODE_SETUP.md) — step-by-step MCP configuration
- [Quick Start](docs/QUICKSTART.md) — first automation in 5 minutes
- [Examples](docs/EXAMPLES.md) — common workflows and patterns
- [Headless Mode](docs/HEADLESS_MODE.md) — hidden desktop architecture and tool compatibility

## Contributing

Contributions welcome! See [issues](https://github.com/rhom6us/winforms-mcp/issues) for open items.

## License

[MIT](LICENSE)
