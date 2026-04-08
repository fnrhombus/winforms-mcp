# fnWindowsMCP Quick Start Guide

Get up and running with fnWindowsMCP in 5 minutes.

## Prerequisites

- Windows 10 or later
- .NET 8.0 SDK or later
- (Optional) Visual Studio Code or Visual Studio 2022

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/yourusername/fnWindowsMCP.git
cd fnWindowsMCP
```

### 2. Build the Solution

```bash
dotnet build
```

### 3. Verify Installation

```bash
dotnet test
```

You should see tests passing (some may require automation setup).

## Your First Automation

### Step 1: Start the MCP Server

```bash
dotnet run --project src/fnWindowsMCP.Server/fnWindowsMCP.Server.csproj
```

The server is now listening on stdin/stdout for JSON-RPC messages.

### Step 2: Launch Notepad (in another terminal)

Send this JSON message to the server:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "launch_app",
    "arguments": {
      "path": "notepad.exe"
    }
  }
}
```

### Step 3: Type Some Text

First, find a text element and interact with it:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "send_keys",
    "arguments": {
      "keys": "Hello from fnWindowsMCP!"
    }
  }
}
```

### Step 4: Take a Screenshot

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "take_screenshot",
    "arguments": {
      "outputPath": "C:\\temp\\my_first_automation.png"
    }
  }
}
```

## Testing with TestApp

### Run the Test Application

```bash
dotnet run --project src/fnWindowsMCP.TestApp/fnWindowsMCP.TestApp.csproj
```

A WinForms window appears with various controls.

### Interact with It

Launch the server and run:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "textBox"
    }
  }
}
```

This will return the element ID.

Then type text:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "type_text",
    "arguments": {
      "elementId": "elem_1",
      "text": "Test input",
      "clearFirst": true
    }
  }
}
```

## Common Tasks

### Find an Element by Name

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "buttonName"
    }
  }
}
```

### Click a Button

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "elem_1"
    }
  }
}
```

### Enter Text in a Field

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "type_text",
    "arguments": {
      "elementId": "elem_1",
      "text": "Your text here",
      "clearFirst": true
    }
  }
}
```

### Wait for Element

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "wait_for_element",
    "arguments": {
      "automationId": "successLabel",
      "timeoutMs": 5000
    }
  }
}
```

### Check Element Exists

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "element_exists",
    "arguments": {
      "automationId": "errorMessage"
    }
  }
}
```

### Get Element Property

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_property",
    "arguments": {
      "elementId": "elem_1",
      "propertyName": "name"
    }
  }
}
```

### Capture Screenshot

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "take_screenshot",
    "arguments": {
      "outputPath": "C:\\temp\\screen.png"
    }
  }
}
```

### Close Application

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "close_app",
    "arguments": {
      "pid": 5432,
      "force": false
    }
  }
}
```

## Project Structure

```
fnWindowsMCP/
├── src/
│   ├── fnWindowsMCP.Server/     # MCP server (main)
│   └── fnWindowsMCP.TestApp/    # Test WinForms app
├── tests/
│   └── fnWindowsMCP.Tests/      # NUnit tests
├── examples/
│   └── EXAMPLES.md              # Detailed examples
├── README.md                    # Full documentation
└── QUICKSTART.md               # This file
```

## Available Tools

1. **find_element** - Locate UI element
2. **click_element** - Click on element
3. **type_text** - Enter text into field
4. **set_value** - Set element value
5. **get_property** - Read element properties
6. **launch_app** - Start application
7. **attach_to_process** - Connect to running app
8. **close_app** - Close application
9. **take_screenshot** - Capture screen
10. **element_exists** - Check element presence
11. **wait_for_element** - Wait for element to appear
12. **drag_drop** - Drag and drop
13. **send_keys** - Send keyboard input

## Troubleshooting

### "Element not found"

1. Use `element_exists` to verify
2. Use `wait_for_element` to wait for loading
3. Increase timeout value
4. Check that the element's Name property is set

### "Failed to launch application"

1. Verify the full path to executable
2. Ensure executable exists and is not corrupted
3. Try with a simple app like `notepad.exe` first

### Server not responding

1. Ensure server is running
2. Check for error messages in console
3. Verify JSON format is correct
4. Ensure each message is on a single line

## Next Steps

1. Read [EXAMPLES.md](examples/EXAMPLES.md) for detailed workflows
2. Run the [TestApp](src/fnWindowsMCP.TestApp/) to explore controls
3. Check the [API Reference](README.md#mcp-tool-reference)
4. Run the [test suite](tests/fnWindowsMCP.Tests/) to verify

## Performance Tips

- Use `element_exists` to check before interaction (faster than catching errors)
- Increase timeouts for slow applications
- Use `wait_for_element` strategically (don't wait unnecessarily)
- Batch multiple operations when possible
- Close applications immediately after use

## Integration with Claude Code

To use fnWindowsMCP with Claude Code, configure it as an MCP server in your workspace:

```json
{
  "mcp": {
    "fnWindowsMCP": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/fnWindowsMCP.Server/fnWindowsMCP.Server.csproj"]
    }
  }
}
```

Then Claude Code can call the automation tools directly!

---

Happy automating! For more help, see [README.md](README.md) or [EXAMPLES.md](examples/EXAMPLES.md).
