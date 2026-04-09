# WinForms MCP — Complete Gap Analysis & Implementation Plan

## Context

The Rhombus.WinFormsMcp MCP server provides headless automation for WinForms applications. The core design→build→run→test→iterate development loop **works today** for simple-to-moderate apps. This plan identifies every gap preventing fully autonomous development of *complex* WinForms apps, organized into implementation phases by priority.

Another Claude instance is concurrently working on headless mode tooling — those changes are out of scope here.

## Current State: 19 Tools (12 visible, 7 hidden, 2 stubs)

### Visible to MCP clients (in `GetToolDefinitions()`)
`find_element`, `click_element`, `type_text`, `get_property`, `launch_app`, `get_process_status`, `take_screenshot`, `render_form`, `select_item`, `click_menu_item`, `get_element_tree`, plus `raise_event`/`listen_for_event` (stubs)

### Fully implemented but MISSING from `GetToolDefinitions()`
`set_value`, `attach_to_process`, `close_app`, `element_exists`, `wait_for_element`, `drag_drop`, `send_keys`

---

## Phase 0 — Critical Bug Fix (do first)

**Goal:** Make existing tools discoverable. Zero new code — just add schema definitions.

### 0.1 Add 7 missing tool definitions to `GetToolDefinitions()`

**File:** `src/Rhombus.WinFormsMcp.Server/Program.cs` — `GetToolDefinitions()` method

| Tool | Parameters to Document |
|------|----------------------|
| `set_value` | `elementId` (string, required), `value` (string, required) |
| `attach_to_process` | `pid` (int, optional), `processName` (string, optional) — one required |
| `close_app` | `pid` (int, required), `force` (bool, optional, default false) |
| `element_exists` | `automationId` (string, required) |
| `wait_for_element` | `automationId` (string, required), `timeoutMs` (int, optional, default 10000) |
| `drag_drop` | `sourceElementId` (string, required), `targetElementId` (string, required) |
| `send_keys` | `keys` (string, required), `pid` (int, optional) |

**Impact:** Immediately takes visible tool count from 12 → 19. Agents gain access to process lifecycle, async waits, keyboard input, and drag-drop.

**Verification:** Run MCP server, send `tools/list` request, confirm all 19 tools appear with correct schemas.

---

## Phase 1 — Reliability & Async (high impact, moderate effort)

**Goal:** Make the agent reliable when interacting with apps that have async operations, loading states, or long-running tasks.

### 1.1 `wait_for_condition` tool

**Problem:** `wait_for_element` only checks element *existence*. Can't wait for "button becomes enabled", "label text changes to Done", or "progress bar reaches 100%". Agents must poll with `get_property` in a retry loop — clunky and wastes tool calls.

**Design:**
```
wait_for_condition(elementId, propertyName, expectedValue, comparison?, timeoutMs?)
```
- `comparison`: `equals` (default), `contains`, `not_equals`, `greater_than`, `less_than`
- Polls at 100ms intervals (same as `wait_for_element`)
- Returns `{ found: true/false, actualValue, elapsedMs }`

**Files to modify:**
- `IAutomationHelper.cs` — add `WaitForConditionAsync` method
- `AutomationHelper.cs` — implement using existing `GetProperty` + polling loop
- `Program.cs` — add tool definition and handler

### 1.2 `toggle_element` tool

**Problem:** `click_element` uses InvokePattern, which doesn't reliably toggle checkboxes/radio buttons. TogglePattern is the correct UIA pattern for these controls.

**Design:**
```
toggle_element(elementId, desiredState?)
```
- `desiredState`: `on`, `off`, `indeterminate`, or omit to just toggle
- Uses TogglePattern.Toggle() and checks result
- Returns `{ previousState, currentState }`

**Files to modify:**
- `IAutomationHelper.cs` — add `Toggle` method
- `AutomationHelper.cs` — implement via TogglePattern
- `Program.cs` — add tool definition and handler

**Verification:** Launch TestApp, toggle checkboxes and radio buttons, confirm state changes via `get_property`.

---

## Phase 2 — Data-Heavy Apps (high impact, higher effort)

**Goal:** Enable working with DataGridView, large lists, and scrollable content — the most common WinForms controls after basic inputs.

### 2.1 `scroll_element` tool

**Problem:** No way to scroll within ListBox, DataGridView, TreeView, or Panel. Off-screen items are unreachable. `select_item` handles combo/list *selection* but not general scrolling.

**Design:**
```
scroll_element(elementId, direction, amount?, scrollType?)
```
- `direction`: `up`, `down`, `left`, `right`
- `amount`: number of scroll units (default 1)
- `scrollType`: `line` (default) or `page`
- Uses ScrollPattern.Scroll() or ScrollPattern.SetScrollPercent()
- Returns `{ horizontalPercent, verticalPercent, horizontallyScrollable, verticallyScrollable }`

**Files to modify:**
- `IAutomationHelper.cs` — add `Scroll` method
- `AutomationHelper.cs` — implement via ScrollPattern
- `Program.cs` — add tool definition and handler

### 2.2 `get_table_data` tool

**Problem:** DataGridView is one of the most-used WinForms controls. No way to read cell values, get row/column counts, or understand grid structure. `get_element_tree` shows some structure but it's fragile and verbose.

**Design:**
```
get_table_data(elementId, startRow?, rowCount?, columns?)
```
- Returns structured JSON: `{ rowCount, columnCount, headers: [...], rows: [{ cells: [...] }] }`
- `startRow` + `rowCount` for pagination (grids can have thousands of rows)
- `columns` to filter specific columns by index or header name
- Uses TablePattern + GridPattern to read cells

**Files to modify:**
- `IAutomationHelper.cs` — add `GetTableData` method
- `AutomationHelper.cs` — implement via TablePattern/GridPattern
- `Program.cs` — add tool definition and handler

### 2.3 `set_table_cell` tool

**Problem:** Can't edit DataGridView cells programmatically.

**Design:**
```
set_table_cell(elementId, row, column, value)
```
- `column` accepts index (int) or header name (string)
- Navigates to cell via GridPattern.GetItem(), sets via ValuePattern
- Returns `{ previousValue, newValue }`

**Files to modify:** Same as 2.2 — extend the same interface/implementation.

**Verification:** Add a DataGridView to TestApp. Use `get_table_data` to read, `set_table_cell` to edit, `scroll_element` to page through large datasets.

---

## Phase 3 — Window & Multi-Form Management (medium impact, moderate effort)

**Goal:** Support apps with multiple windows, dialogs, resizable layouts, and MDI interfaces.

### 3.1 `manage_window` tool

**Problem:** Can't resize, move, minimize, maximize, or restore windows. Important for testing responsive layouts and verifying window behavior.

**Design:**
```
manage_window(pid, action, width?, height?, x?, y?)
```
- `action`: `maximize`, `minimize`, `restore`, `resize`, `move`, `close`
- `resize` uses `width`/`height`; `move` uses `x`/`y`
- Uses WindowPattern (SetWindowVisualState, Resize, Move)
- Returns `{ windowState, boundingRectangle }`

**Files to modify:**
- `IAutomationHelper.cs` — add `ManageWindow` method
- `AutomationHelper.cs` — implement via WindowPattern
- `Program.cs` — add tool definition and handler

### 3.2 `list_windows` tool

**Problem:** Apps with multiple forms (MDI children, dialogs, popups, tool windows) are hard to navigate. `find_element` and `get_element_tree` start from the main window. No way to discover other windows belonging to the same process.

**Design:**
```
list_windows(pid)
```
- Enumerates all top-level windows for the process
- Returns array: `[{ hwnd, title, className, isVisible, isModal, boundingRectangle, elementId }]`
- Each window gets cached as an element for use as `parent` in subsequent `find_element` calls

**Files to modify:**
- `IAutomationHelper.cs` — add `ListWindows` method
- `AutomationHelper.cs` — implement via process window enumeration + UIA
- `Program.cs` — add tool definition and handler

### 3.3 `get_focused_element` tool

**Problem:** Can't determine which element has keyboard focus. Needed for debugging tab order, verifying focus after interactions, and understanding keyboard navigation flow.

**Design:**
```
get_focused_element(pid?)
```
- Returns the currently focused element's properties + caches it with an elementId
- Uses `AutomationElement.FocusedElement` (scoped to process if pid provided)

**Files to modify:**
- `Program.cs` — add tool definition and handler (uses existing FlaUI API, minimal AutomationHelper change)

**Verification:** Launch TestApp with multiple windows. Use `list_windows` to discover them, `manage_window` to resize, `get_focused_element` to verify tab order.

---

## Phase 4 — Event System & Context Menus (lower impact, higher complexity)

**Goal:** Enable event-driven testing and complete interaction coverage.

### 4.1 Implement `raise_event` (currently a stub)

**Problem:** Can't programmatically trigger UI events (Validated, Click, custom events). Currently returns "not yet implemented".

**Design:**
```
raise_event(elementId, eventName, eventArgs?)
```
- Limited to UIA-raisable patterns: Invoke, Toggle, ExpandCollapse, SelectionItem
- `eventName` maps to pattern methods: `invoke` → InvokePattern.Invoke(), `toggle` → TogglePattern.Toggle(), etc.
- This is a thin wrapper around existing patterns — the value is explicit intent ("raise an event" vs "click")

**Scope note:** True arbitrary .NET event raising (e.g., `Form.FormClosing`) is not feasible via UIA. Document this limitation clearly.

### 4.2 Implement `listen_for_event` (currently a stub)

**Problem:** Can't wait for async UI events. Agent can't know when a background operation completes unless it polls.

**Design:**
```
listen_for_event(pid?, elementId?, eventType, timeoutMs?)
```
- `eventType`: `structure_changed`, `property_changed`, `focus_changed`, `window_opened`, `window_closed`
- Registers a UIA event handler, waits for it to fire or timeout
- Returns `{ fired: true/false, eventDetails, elapsedMs }`
- Uses FlaUI's event handler registration

**Complexity note:** UIA event handlers require COM apartment management and careful cleanup. This is the most complex item in the plan.

### 4.3 `open_context_menu` tool

**Problem:** No way to right-click an element to open its context menu. Workarounds (`send_keys` with Shift+F10) only work on default desktop.

**Design:**
```
open_context_menu(elementId)
```
- Attempts LegacyIAccessible pattern first, then falls back to mouse right-click
- Returns the context menu element (cached) for use with `click_menu_item`

**Verification:** Add context menu to TestApp. Use `open_context_menu` → `click_menu_item` chain. Test `listen_for_event` with window open/close events.

---

## Phase 5 — Polish & Edge Cases (nice to have)

**Goal:** Round out the toolkit for completeness.

### 5.1 `get_clipboard` / `set_clipboard` tools
- Read/write clipboard programmatically
- Useful for verifying copy/paste operations without `send_keys`
- Requires STA thread (clipboard is COM-based)

### 5.2 `read_tooltip` tool
- Capture tooltip text from an element
- Uses ToolTip UIA pattern or HelpText property
- Ephemeral UI — may need a "hover then read" approach

### 5.3 `find_elements` (plural) tool
- Return *all* matches, not just the first
- Useful for "find all buttons" or "find all list items"
- Uses existing `FindAll` method on IAutomationHelper (already implemented, just not exposed)

---

## Summary

| Phase | Items | Effort | Impact | Cumulative Coverage |
|-------|-------|--------|--------|-------------------|
| **0** | Fix 7 hidden tool definitions | ~1 hour | Critical — unblocks existing functionality | 85% → 85% (visible) |
| **1** | `wait_for_condition`, `toggle_element` | ~3 hours | Reliable async + checkbox/radio | 85% → 90% |
| **2** | `scroll_element`, `get_table_data`, `set_table_cell` | ~5 hours | Data-heavy app support | 90% → 95% |
| **3** | `manage_window`, `list_windows`, `get_focused_element` | ~4 hours | Multi-form + responsive testing | 95% → 97% |
| **4** | `raise_event`, `listen_for_event`, `open_context_menu` | ~6 hours | Event-driven testing | 97% → 99% |
| **5** | Clipboard, tooltips, find_elements | ~3 hours | Edge case completeness | 99% → ~100% |

**Phase 0 should be done immediately.** Phases 1–2 get you to 95% and cover the vast majority of real-world WinForms apps. Phases 3–5 are diminishing returns but valuable for complex enterprise apps.
