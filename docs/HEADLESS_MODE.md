# Headless Mode — Technical Reference

This document is for AI agents (Claude, etc.) that drive WinForms MCP tools. It explains how headless mode works, what's different, and which tools have limitations.

## How It Works

When `HEADLESS=true`, the MCP server creates a **hidden Windows desktop** (`CreateDesktop` API) within the same window station (`WinSta0`). Processes launched via `launch_app` run on this hidden desktop. The desktop is invisible — it is never displayed on any monitor.

```
User's Desktop (Default)          Hidden Desktop (McpAutomation)
┌──────────────────────┐          ┌──────────────────────┐
│  IDE, browser, etc.  │          │  Launched WinForms   │
│  (user is working)   │          │  app under test      │
│                      │          │                      │
│  No disruption.      │          │  Full HWND, full     │
│  No focus stealing.  │          │  UIA tree, full      │
│  No flashing.        │          │  PrintWindow capture. │
└──────────────────────┘          └──────────────────────┘
        ▲                                  ▲
        │ attach_to_process                │ launch_app
        │ (default desktop)                │ (hidden desktop)
        └──────────────┬───────────────────┘
                       │
                MCP Server routes
                each tool call to
                the correct desktop
```

## Per-Process Desktop Routing

The server tracks which desktop each process lives on:

- `launch_app` (headless) → hidden desktop
- `attach_to_process` → default desktop (always)
- `launch_app` (non-headless) → default desktop

**You don't need to think about desktops.** The server automatically switches to the correct desktop before each UIA operation and uses PrintWindow for screenshots regardless of desktop. This is transparent.

## Tool Compatibility

### Full support on hidden desktop

These tools use UIA patterns (COM calls) or PrintWindow and work identically on both desktops:

| Tool | Mechanism |
|------|-----------|
| `find_element` | UIA tree traversal |
| `element_exists` | UIA tree search |
| `wait_for_element` | UIA polling |
| `get_element_tree` | UIA recursive traversal |
| `get_property` | UIA pattern reads (ValuePattern, TogglePattern, etc.) |
| `click_element` | UIA InvokePattern / TogglePattern / ExpandCollapsePattern |
| `type_text` | UIA ValuePattern.SetValue() |
| `set_value` | UIA ValuePattern.SetValue() |
| `select_item` | UIA SelectionItemPattern / ExpandCollapsePattern |
| `click_menu_item` | UIA InvokePattern / ExpandCollapsePattern |
| `take_screenshot` | Win32 PrintWindow (PW_RENDERFULLCONTENT) |
| `launch_app` | Win32 CreateProcess with lpDesktop |
| `close_app` | Process.Kill / WM_CLOSE |
| `get_process_status` | Process queries + native GetExitCodeProcess |
| `render_form` | DesignSurface (in-process, no desktop dependency) |

### Default desktop only

These tools use input simulation (`SendInput` / `SendKeys`) which targets the active desktop's input queue. They only work for processes on the user's visible desktop:

| Tool | Why |
|------|-----|
| `send_keys` | Keyboard simulation via `SendKeys.SendWait()` targets the active desktop |
| `drag_drop` | Mouse cursor positioning + keyboard simulation |
| `click_element` (double-click) | FlaUI's `DoubleClick()` uses mouse simulation |

**If you need to type into a headless app, use `type_text` or `set_value` instead of `send_keys`.** They use UIA ValuePattern which works across desktops.

**If you need to click a headless element, use `click_element` (single click).** It uses InvokePattern. Avoid double-click on headless elements.

## Mixed Mode Sessions

You can freely mix headless and visible processes in the same session:

```
launch_app("MyApp.exe")              → headless (hidden desktop)
attach_to_process("notepad")         → visible (default desktop)
find_element(automationId: "btn1")   → server routes to correct desktop
click_element(elementId: "elem_1")   → server routes to correct desktop
take_screenshot(pid: 1234)           → PrintWindow works on either desktop
send_keys("^s")                      → only reaches the active (visible) desktop
```

## Screenshots

`take_screenshot` uses Win32 `PrintWindow` with `PW_RENDERFULLCONTENT` (flag 2). This captures the window's rendered content regardless of:

- Whether the window is on the hidden or default desktop
- Whether the window is off-screen, occluded, or behind other windows
- Whether DWM composition is active

Provide `pid` to `take_screenshot` for the most reliable capture path. The server will find the process's HWND (via `EnumDesktopWindows` for hidden desktop processes) and capture it directly.

## Limitations and Workarounds

| Limitation | Workaround |
|-----------|------------|
| `send_keys` doesn't reach hidden desktop | Use `type_text` / `set_value` for text input |
| `drag_drop` doesn't work on hidden desktop | Use `select_item` or `click_element` sequences |
| Double-click uses mouse simulation | Use single `click_element` (InvokePattern) |
| `click_element` fallback to mouse simulation on controls without InvokePattern | Rare for standard WinForms controls; use `get_property` to check state instead |

## CI/CD Notes

GitHub Actions `windows-latest` runners have an interactive desktop session. Headless mode works there, but it's also fine to run without headless mode since there's no human user to disturb. Headless mode is most valuable on developer machines where the MCP is running in the background while the developer works.
