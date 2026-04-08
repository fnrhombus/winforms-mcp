# Claude Code MCP Setup Guide

This guide explains how to configure Claude Code to use the **Rhombus.WinFormsMcp** MCP server for Windows Forms automation.

## Prerequisites

- **Claude Code** - CLI tool installed and configured
- **Windows 10/11** - x64 architecture required
- **.NET 8.0 Runtime** or SDK installed
- **Node.js** (optional, if using npm distribution)

## Installation Methods

### Option 1: NuGet Package (for .NET projects)

Install the NuGet package into your project:

```powershell
Install-Package Rhombus.WinFormsMcp
```

Then configure Claude Code to use it (see [Configuration](#configuration) below).

### Option 2: NPM Package (for Node.js/JavaScript projects)

Install globally:

```bash
npm install -g @rhom6us/winforms-mcp
```

Or use directly with `npx`:

```bash
npx @rhom6us/winforms-mcp
```

### Option 3: Direct Download

Download the latest release from [GitHub Releases](https://github.com/rhom6us/winforms-mcp/releases).

Extract and run:

```bash
./Rhombus.WinFormsMcp.Server.exe
```

## Configuration

### For Claude Code CLI

Claude Code uses MCP servers configured in `~/.claude/mcp.json` (or `%USERPROFILE%\.claude\mcp.json` on Windows).

#### Step 1: Locate or Create the Config File

If it doesn't exist, create it:

```bash
# macOS/Linux
mkdir -p ~/.claude
touch ~/.claude/mcp.json

# Windows PowerShell
mkdir -Force $env:USERPROFILE\.claude
New-Item -Path "$env:USERPROFILE\.claude\mcp.json" -ItemType File
```

#### Step 2: Add the MCP Server Configuration

Edit `~/.claude/mcp.json` and add the server configuration. Choose one based on your installation method:

**For NuGet/Direct Installation:**

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "dotnet",
      "args": [
        "path/to/Rhombus.WinFormsMcp.Server.dll"
      ],
      "env": {
        "HEADLESS": "true"
      }
    }
  }
}
```

**For NPM Global Installation:**

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "npx",
      "args": [
        "@rhom6us/winforms-mcp"
      ],
      "env": {
        "HEADLESS": "true"
      }
    }
  }
}
```

**For Local Executable:**

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "C:\\path\\to\\Rhombus.WinFormsMcp.Server.exe",
      "args": [],
      "env": {
        "HEADLESS": "true"
      }
    }
  }
}
```

#### Step 3: Verify Installation

Test that Claude Code recognizes the MCP server:

```bash
claude-code --list-mcp-servers
```

You should see `winforms-mcp` in the output.

## Usage in Claude Code

Once configured, you can use the WinForms automation tools directly in Claude Code:

### Example 1: Launch an Application

```claude
Let me automate a Windows Forms application.

@mcp winforms-mcp launch_app {
  "path": "C:\\Program Files\\MyApp\\MyApp.exe",
  "arguments": "--debug"
}
```

### Example 2: Find and Click a Button

```claude
@mcp winforms-mcp find_element {
  "automationId": "okButton"
}

@mcp winforms-mcp click_element {
  "elementId": "elem_1"
}
```

### Example 3: Fill a Form

```claude
@mcp winforms-mcp find_element {
  "name": "textBox1"
}

@mcp winforms-mcp type_text {
  "elementId": "elem_1",
  "text": "John Doe",
  "clearFirst": true
}
```

### Example 4: Take a Screenshot

```claude
@mcp winforms-mcp take_screenshot {
  "outputPath": "C:\\temp\\screenshot.png"
}
```

## Available Tools

The MCP server exposes 14 tools for WinForms automation:

### Process Management
- `launch_app` - Start a new application
- `attach_to_process` - Connect to a running process
- `close_app` - Terminate an application

### Element Discovery
- `find_element` - Locate UI elements by various properties
- `element_exists` - Check if an element exists

### UI Interaction
- `click_element` - Click or double-click elements
- `type_text` - Enter text into fields
- `set_value` - Set element values
- `drag_drop` - Perform drag-and-drop operations
- `send_keys` - Send keyboard input

### Validation & Monitoring
- `take_screenshot` - Capture the UI state
- `wait_for_element` - Wait for elements to appear
- `get_property` - Read element properties

### Form Preview
- `render_form` - Render a .Designer.cs file to a PNG image preview (no build required)

For detailed tool documentation, see [MCP Tool Reference](../README.md#mcp-tool-reference).

## Form Design Workflow

The `render_form` tool enables a rapid edit → render → iterate cycle for WinForms form design:

1. **Edit** the `.Designer.cs` file to add or modify controls
2. **Render** with `render_form` to see a PNG preview
3. **Review** the rendered output and adjust positions, sizes, or properties
4. **Repeat** until the layout matches the design intent

This works even when the project doesn't build, since `render_form` uses Roslyn parsing (not compilation) and reflection to instantiate standard WinForms controls.

### Example

```claude
@mcp winforms-mcp render_form {
  "designerFilePath": "C:\\MyProject\\Forms\\MainForm.Designer.cs",
  "outputPath": "C:\\temp\\preview.png"
}
```

### Add to Your Project's CLAUDE.md

Add the following to your project's `CLAUDE.md` to guide Claude Code when editing designer files:

````markdown
## WinForms Designer File Guidelines

When editing `.Designer.cs` files, follow these conventions so `render_form` can preview them:

1. **Use fully-qualified type names** — `System.Windows.Forms.Button`, not `Button`
2. **Prefix field access with `this.`** — `this.button1`, not `button1`
3. **Include `InitializeComponent()`** in a partial class
4. **Wrap layout changes** with `SuspendLayout()` / `ResumeLayout(false)` / `PerformLayout()`
5. **Only set non-default properties** — the designer only serializes values that differ from defaults
6. **Order**: instantiation → SuspendLayout → per-control properties → form properties/Controls.Add → ResumeLayout

After editing a designer file, use `render_form` to preview:
```
@mcp winforms-mcp render_form {
  "designerFilePath": "<path to .Designer.cs>",
  "outputPath": "<path to output .png>"
}
```
````

## Troubleshooting

### Server Not Recognized

If Claude Code doesn't recognize the server:

1. Verify the config file syntax:
   ```bash
   cat ~/.claude/mcp.json
   ```

2. Check that the command path is correct:
   - For NuGet: Full path to the `.dll` file
   - For NPM: Run `which npx` or `where npx` to verify
   - For executable: Full path to `.exe` file

3. Restart Claude Code:
   ```bash
   # Close all Claude Code windows and restart
   ```

### Connection Issues

If you get connection errors when using tools:

1. Test the MCP server directly:
   ```bash
   # For .NET
   dotnet path/to/Rhombus.WinFormsMcp.Server.dll

   # For NPM
   npx @rhom6us/winforms-mcp

   ```

2. Verify you're running on Windows x64
3. Ensure .NET 8.0 runtime is installed: `dotnet --version`

### Application Launch Failures

If `launch_app` fails:

1. Verify the application path exists
2. Ensure the path uses Windows format: `C:\path\to\app.exe`
3. Check that you have permissions to run the application
4. Try using a full absolute path instead of relative paths

### Element Not Found

If `find_element` can't locate elements:

1. Use `take_screenshot` to verify the application is visible
2. Check the AutomationId or Name properties match exactly
3. Try increasing the timeout or waiting for the element first:
   ```claude
   @mcp winforms-mcp wait_for_element {
     "automationId": "myButton",
     "timeoutMs": 10000
   }
   ```

## Advanced Configuration

### Environment Variables

You can pass environment variables to the MCP server:

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "dotnet",
      "args": ["path/to/server.dll"],
      "env": {
        "HEADLESS": "true",
        "FNWINDOWSMCP_TIMEOUT": "5000",
        "FNWINDOWSMCP_SCREENSHOT_DIR": "C:\\temp\\screenshots"
      }
    }
  }
}
```

### Multiple Instances

To run multiple instances (for parallel automation):

```json
{
  "mcpServers": {
    "winforms-mcp-1": {
      "command": "dotnet",
      "args": ["path/to/server.dll"],
      "env": {
        "HEADLESS": "true"
      }
    },
    "winforms-mcp-2": {
      "command": "dotnet",
      "args": ["path/to/server.dll"],
      "env": {
        "HEADLESS": "true"
      }
    }
  }
}
```

Then use them in Claude Code:

```claude
@mcp winforms-mcp-1 launch_app {...}
@mcp winforms-mcp-2 launch_app {...}
```

## Example Workflows

### Complete Test Automation

```claude
I need to automate testing a WinForms application.

Step 1: Launch the application
@mcp winforms-mcp launch_app {
  "path": "C:\\apps\\TestApp.exe"
}

Step 2: Wait for it to load
@mcp winforms-mcp wait_for_element {
  "automationId": "mainWindow",
  "timeoutMs": 5000
}

Step 3: Find the test input field
@mcp winforms-mcp find_element {
  "name": "inputField"
}

Step 4: Enter test data
@mcp winforms-mcp type_text {
  "elementId": "elem_1",
  "text": "test value",
  "clearFirst": true
}

Step 5: Click the submit button
@mcp winforms-mcp find_element {
  "automationId": "submitButton"
}

@mcp winforms-mcp click_element {
  "elementId": "elem_2"
}

Step 6: Capture the result
@mcp winforms-mcp take_screenshot {
  "outputPath": "C:\\results\\test_result.png"
}

Step 7: Clean up
@mcp winforms-mcp close_app {
  "pid": 12345,
  "force": false
}
```

### Visual UI Testing

```claude
Let me verify the UI state of the application.

@mcp winforms-mcp take_screenshot {
  "outputPath": "C:\\screenshots\\ui_state.png"
}

Now I'll check if specific elements are visible:

@mcp winforms-mcp element_exists {
  "automationId": "successMessage"
}

@mcp winforms-mcp get_property {
  "elementId": "elem_1",
  "propertyName": "isEnabled"
}
```

## Support & Documentation

- **GitHub Repository**: https://github.com/rhom6us/winforms-mcp
- **Issues**: https://github.com/rhom6us/winforms-mcp/issues
- **NuGet Package**: https://www.nuget.org/packages/Rhombus.WinFormsMcp
- **NPM Package**: https://www.npmjs.com/package/@rhom6us/winforms-mcp

## Related Resources

- [Claude Code Documentation](https://docs.claude.com/claude-code)
- [Model Context Protocol Specification](https://modelcontextprotocol.io)
- [Windows UI Automation Documentation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview)
- [FlaUI Documentation](https://github.com/Roemer/FlaUI)
