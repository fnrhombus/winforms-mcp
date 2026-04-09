<p align="center">
  <h1 align="center">Rhombus.WinFormsMcp</h1>
  <p align="center"><strong>Give AI agents eyes and hands for Windows Forms apps</strong></p>
</p>

<p align="center">
  <a href="https://github.com/fnrhombus/winforms-mcp/actions/workflows/ci.yml"><img src="https://github.com/fnrhombus/winforms-mcp/actions/workflows/ci.yml/badge.svg" alt="CI Status"></a>
  <a href="https://www.nuget.org/packages/Rhombus.WinFormsMcp"><img src="https://img.shields.io/nuget/v/Rhombus.WinFormsMcp" alt="NuGet Version"></a>
  <a href="https://www.npmjs.com/package/@fnrhombus/winforms-mcp"><img src="https://img.shields.io/npm/v/@fnrhombus/winforms-mcp" alt="NPM Version"></a>
  <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
</p>

<p align="center">
  An <a href="https://modelcontextprotocol.io">MCP server</a> that lets Claude (and any MCP-compatible agent) launch, automate, screenshot, and preview WinForms applications — without touching a mouse.
</p>

---

## The problem

You're building a WinForms app with an AI coding agent. The agent can read and write your code, but it's **blind** — it can't see what the form looks like, can't click a button to test a workflow, and can't tell if the UI it just generated actually renders correctly.

## The fix

One line in your MCP config. Now the agent can:

| | |
|---|---|
| **See forms without building** | `render_form` turns any `.Designer.cs` into a pixel-accurate PNG — the same rendering pipeline Visual Studio uses |
| **Drive running apps** | Launch processes, find elements by name/type/ID, click, type, drag-drop, take screenshots |
| **Work in the background** | Headless mode runs apps on a hidden desktop — zero focus stealing, zero disruption |
| **Target any framework** | .NET Framework 4.x, .NET Core 3.x, .NET 5–9+. Auto-detected from your `.csproj` |

### `render_form` in action

The agent reads your `.Designer.cs`, calls `render_form`, and gets back this — no build, no running app:

<p align="center">
  <img src="docs/images/render-form-demo.png" alt="render_form output — Address Entry Form" width="400">
</p>

It can then edit the layout, re-render, and iterate — a full visual feedback loop with no IDE, no build, and no human in the loop.

## Quick start

Add to your MCP config and restart. Nothing else to install.

**npx** (requires Node.js):

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "npx",
      "args": ["-y", "@fnrhombus/winforms-mcp"]
    }
  }
}
```

**Standalone** (no Node required) — [download the zip from Releases](https://github.com/fnrhombus/winforms-mcp/releases), extract it, and point to the exe:

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "C:/path/to/winformsmcp/winformsmcp.exe"
    }
  }
}
```

> **Note:** Keep the extracted folder structure intact — `render_form` needs the `rendererhost/` subdirectory alongside the exe.

That's it. The agent can now see and interact with any WinForms application on your machine.

## Tools

| Category | Tools | Description |
|----------|-------|-------------|
| **Process** | `launch_app` `attach_to_process` `close_app` `get_process_status` | Start, attach to, and manage Windows processes |
| **Discovery** | `find_element` `element_exists` `wait_for_element` `get_element_tree` | Locate UI elements by AutomationId, name, class, or control type |
| **Interaction** | `click_element` `type_text` `set_value` `select_item` `click_menu_item` `drag_drop` `send_keys` | Click buttons, fill text boxes, select combo items, navigate menus |
| **Visual** | `take_screenshot` `render_form` | Capture running apps or render `.Designer.cs` to PNG |

## Cross-framework rendering

`render_form` detects your project's target framework and dispatches to a matching out-of-process host:

| Your project targets | Renderer host |
|---|---|
| .NET Framework 4.0–4.8.x | `net48` |
| .NET Core 3.x | `netcoreapp3.1` |
| .NET 5, 6, 7, 8, 9+ | `net8.0-windows` |

Custom controls, third-party components, and framework-specific APIs all resolve correctly. Override with the `TFM` environment variable if needed.

## Headless mode

Set `HEADLESS=true` to launch apps on a hidden Windows desktop (`CreateDesktop` API):

- **Zero focus stealing** — apps can't steal focus, show TopMost windows, or flash in the taskbar
- **Full automation** — element discovery, clicking, typing, and screenshots all work through UIA patterns and PrintWindow
- **Mixed mode** — headless and visible apps in the same session, each automatically routed to its correct desktop

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "npx",
      "args": ["-y", "@fnrhombus/winforms-mcp"],
      "env": { "HEADLESS": "true" }
    }
  }
}
```

> **Note:** `send_keys` and `drag_drop` require input simulation and only work on the visible desktop. Use `type_text`/`set_value` and `click_element` for headless processes.

## Environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `HEADLESS` | `false` | Run launched apps on a hidden desktop |
| `TFM` | `auto` | Lock rendering to a specific framework (`net48`, `netcoreapp3.1`, `net8.0-windows`) |

## Documentation

- [Claude Code Setup Guide](docs/CLAUDE_CODE_SETUP.md) — step-by-step MCP configuration
- [Quick Start](docs/QUICKSTART.md) — first automation in 5 minutes
- [Examples](docs/EXAMPLES.md) — common workflows and patterns
- [Headless Mode](docs/HEADLESS_MODE.md) — hidden desktop architecture and tool compatibility

## Contributing

Contributions welcome! See [issues](https://github.com/fnrhombus/winforms-mcp/issues) for open items.

## License

[MIT](LICENSE)
