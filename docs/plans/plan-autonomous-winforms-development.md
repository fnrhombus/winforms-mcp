# Plan: Autonomous WinForms Development Loop

## 1. Current Capabilities

### What Claude can already do today:

**Form Design & Preview**
- Write forms using the VS designer file convention (.cs + .Designer.cs + .resx)
- Preview forms via three renderers with increasing robustness:
  - `render_form` (~150ms, standard controls only, saves to file)
  - `render_form_inprocess` (~450ms, all controls, returns base64 image -- Claude can SEE this)
  - `render_form_compiled` (~2800ms, full dotnet build, returns base64 image -- Claude can SEE this)
- Iterate on layout by editing .Designer.cs and re-rendering

**Build**
- Run `dotnet build` via bash and read compiler errors
- Run `dotnet restore` to manage NuGet packages

**Runtime Automation (via FlaUI)**
- Launch apps (`launch_app`), attach to running processes (`attach_to_process`)
- Find UI elements by AutomationId, Name, or ClassName (`find_element`)
- Interact: click, type text, set values, drag/drop, send keys
- Query element properties (`get_property`)
- Wait for elements to appear (`wait_for_element`)
- Close apps (`close_app`)

**Testing**
- Run `dotnet test` via bash and read results

---

## 2. Gaps & Weaknesses (ranked by impact)

### CRITICAL -- Breaks the feedback loop

#### Gap 1: `take_screenshot` does NOT return image data to Claude
**Impact: CRITICAL**

`take_screenshot` saves a PNG to disk and returns `{"success": true, "message": "Screenshot saved to ..."}`. It does NOT return base64 image data. The `imageBase64` response pathway (lines 269-289 in Program.cs) only triggers when the JSON result contains an `imageBase64` property, but `TakeScreenshot` never sets this.

This means Claude CANNOT see what a running app looks like. The entire "Run & Validate" step of the development loop is blind. Claude can launch an app, click buttons, and read properties, but cannot visually verify the result.

**Fix:** Change `TakeScreenshot` to return base64 image data (like `render_form_inprocess` does) so Claude can see the running app through the MCP image content block.

#### Gap 2: `render_form` (fastest renderer) does NOT return image data to Claude
**Impact: HIGH**

The fastest renderer saves to a file path and returns a text message. Unlike `render_form_inprocess` and `render_form_compiled`, it does not return `imageBase64`. Claude cannot see the output unless it reads the file with its own tools, which it cannot do for PNG binary files.

**Fix:** Make `render_form` also return base64 image data via the `imageBase64` response pathway, consistent with the other two renderers. Optionally keep the `outputPath` parameter for backward compat but always include `imageBase64` in the response.

#### Gap 3: No runtime error/exception capture from launched apps
**Impact: HIGH**

When Claude launches a WinForms app and it crashes or throws an exception, Claude has no way to know what happened. `launch_app` only returns the PID. If the app crashes silently, Claude sees nothing. If it shows an error dialog, Claude might detect it via `find_element` on the dialog, but cannot read the exception details, stack trace, or stderr output.

**Fix:** New tool `get_process_output` or enhance `launch_app` to capture stdout/stderr from the launched process. Also consider a `get_process_status` tool that reports whether the process is still running, its exit code if terminated, and any captured stderr.

### HIGH -- Significantly limits complexity of apps Claude can build

#### Gap 4: No element tree exploration tool
**Impact: HIGH**

`find_element` requires knowing the exact AutomationId, Name, or ClassName upfront. There is no tool to enumerate the UI tree -- e.g., "list all children of the main window" or "describe the full element hierarchy." The `GetAllChildren` method exists in AutomationHelper but is NOT exposed as an MCP tool.

Without this, Claude cannot discover the UI structure of a running app. This is essential for:
- Debugging layout issues ("what controls are actually visible?")
- Navigating complex UIs (menus, tab controls, tree views)
- Verifying that forms rendered correctly at runtime

**Fix:** New tool `get_element_tree` that returns the UI automation tree (or a subtree) as structured JSON with element names, types, automation IDs, bounding rectangles, and enabled/visible state.

#### Gap 5: No project scaffolding tool
**Impact: MEDIUM-HIGH**

Claude can create individual files, but creating a proper WinForms project from scratch requires:
- Creating a .csproj with the right SDK, TargetFramework (net8.0-windows), UseWindowsForms=true
- Creating a .sln file
- Setting up proper folder structure
- Adding NuGet package references
- Creating Program.cs with Application.Run() boilerplate

Claude can do all of this via bash (`dotnet new winforms`, `dotnet sln add`), but it requires knowing the right incantations. A dedicated tool or at minimum better tool descriptions that guide Claude through project creation would help.

**Fix:** Either (a) add a `scaffold_project` tool that creates a properly structured WinForms project, or (b) add detailed guidance in the tool descriptions / CLAUDE.md about using `dotnet new winforms` and `dotnet sln` commands. Option (b) is simpler and probably sufficient since Claude can run bash.

#### Gap 6: No way to read text content from UI elements
**Impact: MEDIUM-HIGH**

`get_property` supports: name, automationId, className, controlType, isOffscreen, isEnabled. It does NOT support reading:
- Text content (Value pattern -- text box content, label text)
- Selected item (Selection pattern -- combo box, list box)
- Checked state (Toggle pattern -- checkbox, radio button)
- Window title
- Item count in list controls

These are essential for verifying app behavior. "Did typing into the search box produce the right results?" requires reading the actual text values.

**Fix:** Expand `get_property` to support more UIA patterns, or add new tools like `get_text_value`, `get_selection`, `get_toggle_state`. The simplest approach is expanding `get_property` to handle `value`, `text`, `isselected`, `ischecked`, `itemcount`, etc.

### MEDIUM -- Limits certain use cases

#### Gap 7: No menu/context menu interaction
**Impact: MEDIUM**

There is no tool for navigating menus (File > Save As, right-click context menus). FlaUI supports menu patterns, but the current tools only offer generic click/find. Menus require a specific interaction pattern: click the menu bar item, wait for the dropdown, click the submenu item.

**Fix:** New tool `click_menu_item` that takes a menu path like `"File > Save As"` and navigates the menu hierarchy, clicking each level and waiting for the next.

#### Gap 8: No combo box / list box selection tool
**Impact: MEDIUM**

Setting a combo box selection requires: clicking the combo box, finding the item in the dropdown, clicking it. The current tools can do this manually but it is fragile. A dedicated `select_item` tool would handle the pattern reliably.

**Fix:** New tool `select_item` that takes an element ID and a value/index, handles the expand-find-click pattern for combo boxes, list boxes, and similar controls.

#### Gap 9: No scroll support
**Impact: MEDIUM**

There is no way to scroll within a control (list view, data grid, panel with scroll bars). Elements that are off-screen cannot be found or interacted with via the current tools.

**Fix:** New tool `scroll_element` with direction (up/down/left/right) and amount parameters. Use the ScrollPattern from UIA.

#### Gap 10: Event tools are stubs
**Impact: LOW-MEDIUM**

`raise_event` and `listen_for_event` return "not yet implemented." For testing event-driven behavior (button click handlers, data validation), Claude needs to trigger events and observe results. However, for most scenarios, Claude can use `click_element` + `take_screenshot`/`get_property` as a workaround.

**Fix:** Implement `listen_for_event` to subscribe to UIA events (element appeared, property changed, structure changed) with a timeout. This enables waiting for async operations to complete. Lower priority since `wait_for_element` covers the most common case.

#### Gap 11: No data grid / table interaction
**Impact: LOW-MEDIUM**

DataGridView is extremely common in business WinForms apps. There are no specialized tools for reading cell values, selecting rows, or editing cells in a grid. The generic find/click tools are insufficient for grid navigation.

**Fix:** New tools: `get_grid_data` (read a table as JSON), `set_grid_cell` (set a cell value), `select_grid_row` (select a row by index or value).

#### Gap 12: No file dialog handling
**Impact: LOW-MEDIUM**

Open/Save file dialogs are system dialogs, not WinForms controls. They require special handling (typing into the filename box, navigating the folder tree). The current tools can technically interact with them via FlaUI automation but it is not documented or guided.

**Fix:** New tool `handle_file_dialog` that automates the common pattern: wait for dialog, set filename, click Open/Save. Or at minimum, document the approach in tool descriptions.

---

## 3. Proposed New Tools / Enhancements

### Enhancement 1: Make `take_screenshot` return base64 image (CRITICAL)

**Change to existing tool.** No new tool needed.

```
Tool: take_screenshot (enhanced)
Inputs: 
  - elementId (optional): screenshot a specific element
  - outputPath (optional): ALSO save to file (backward compat)
Outputs:
  - imageBase64: PNG as base64 string (returned via MCP image content block)
  - success: true
Behavior:
  - Capture the element or desktop via FlaUI
  - Convert to PNG bytes, then base64
  - Return as {"imageBase64": "..."} so the existing imageBase64 response handler kicks in
  - If outputPath provided, ALSO save to disk
```

### Enhancement 2: Make `render_form` return base64 image (HIGH)

**Change to existing tool.** 

```
Tool: render_form (enhanced)
Inputs:
  - designerFilePath: path to .Designer.cs
  - outputPath (optional, was required): save to file  
Outputs:
  - imageBase64: PNG as base64
Behavior:
  - Render via SyntaxTreeFormRenderer
  - Read the output PNG into bytes, convert to base64
  - Return {"imageBase64": "..."} 
  - Optionally still save to outputPath
```

### New Tool 3: `get_element_tree` (HIGH)

```
Tool: get_element_tree
Description: Get the UI automation element tree for a running application.
  Returns a structured description of all visible controls including their
  type, name, automation ID, bounding rectangle, and enabled/visible state.
  Use this to discover the UI structure before interacting with elements.
Inputs:
  - pid (optional): process ID to get the tree for (uses main window)
  - elementId (optional): root element to enumerate children of
  - depth (optional, default 3): how deep to recurse (1 = immediate children only)
  - maxElements (optional, default 50): cap to prevent huge responses
Outputs:
  - JSON tree structure:
    {
      "name": "MainForm",
      "controlType": "Window",
      "automationId": "MainForm",
      "boundingRectangle": {"x": 0, "y": 0, "width": 800, "height": 600},
      "isEnabled": true,
      "isOffscreen": false,
      "elementId": "elem_5",  // cached for subsequent interaction
      "children": [
        {
          "name": "Save",
          "controlType": "Button",
          "automationId": "btnSave",
          ...
        }
      ]
    }
```

### New Tool 4: `get_process_status` (HIGH)

```
Tool: get_process_status
Description: Check the status of a launched/attached process. Reports whether
  it is still running, its exit code if terminated, and any captured stderr output.
  Use after launch_app to detect crashes or after performing actions to verify
  the app is still responsive.
Inputs:
  - pid: process ID to check
Outputs:
  - isRunning: boolean
  - exitCode: int (if terminated)
  - hasExited: boolean
  - stderr: string (captured stderr output, if any)
  - mainWindowTitle: string (current window title, useful for detecting state changes)
  - responding: boolean (whether the process is responding to Windows messages)
```

**Implementation note:** `launch_app` must be enhanced to redirect StandardError and capture it in the session for later retrieval.

### Enhancement 5: Expand `get_property` to support UIA patterns (MEDIUM-HIGH)

```
Tool: get_property (enhanced)
Additional supported property names:
  - "value" / "text": reads ValuePattern.Value or the element's Name as fallback
  - "ischecked" / "togglestate": reads TogglePattern.ToggleState  
  - "isselected": reads SelectionItemPattern.IsSelected
  - "selecteditem": reads SelectionPattern.Selection[0].Name
  - "items": for list/combo, returns array of item names
  - "itemcount": for list/combo, returns count of items
  - "boundingrectangle": returns {x, y, width, height}
  - "windowtitle": reads the Name of the containing Window element
  - "isexpanded": reads ExpandCollapsePattern state
  - "min"/"max"/"current": reads RangeValuePattern values
```

### New Tool 6: `select_item` (MEDIUM)

```
Tool: select_item
Description: Select an item in a combo box, list box, or similar selection control.
  Handles the expand-find-select pattern automatically.
Inputs:
  - elementId: the combo box / list box element
  - value (optional): item text to select
  - index (optional): item index to select (0-based)
Outputs:
  - success: boolean
  - selectedValue: the text of the selected item
```

### New Tool 7: `click_menu_item` (MEDIUM)

```
Tool: click_menu_item
Description: Navigate and click a menu item in a menu bar or context menu.
  Handles the multi-level click-wait-click pattern for nested menus.
Inputs:
  - menuPath: array of strings representing the menu path, e.g. ["File", "Save As"]
  - pid (optional): target process
Outputs:
  - success: boolean
```

### New Tool 8: `get_grid_data` (LOW-MEDIUM)

```
Tool: get_grid_data
Description: Read data from a DataGridView or similar table control.
Inputs:
  - elementId: the grid element
  - startRow (optional, default 0): first row to read
  - rowCount (optional, default 20): number of rows to read
Outputs:
  - columns: string[] (column headers)
  - rows: string[][] (cell values)
  - totalRows: int
```

---

## 4. Implementation Priority

### Phase 1: Close the Visual Feedback Loop (CRITICAL)
*Goal: Claude can SEE everything it builds and runs.*

1. **`take_screenshot` returns base64** -- Single most impactful change. ~30 min.
   - Read file into bytes, convert to base64, return `imageBase64` in JSON
   - Make `outputPath` optional (not required)
   - Claude can now see running apps through MCP

2. **`render_form` returns base64** -- Consistency with other renderers. ~15 min.
   - Read rendered PNG into bytes, return `imageBase64`
   - Make `outputPath` optional

### Phase 2: Runtime Observability (HIGH)
*Goal: Claude can understand what is happening in a running app.*

3. **`get_element_tree`** -- Lets Claude discover UI structure. ~2 hours.
   - Recursive tree walk with depth/element cap
   - Cache discovered elements for interaction
   - Essential for navigating complex UIs

4. **`get_process_status`** -- Lets Claude detect crashes. ~1 hour.
   - Redirect stderr in launch_app
   - Track exit codes
   - Report responsiveness

5. **Expand `get_property`** -- Lets Claude read control values. ~2 hours.
   - Add ValuePattern, TogglePattern, SelectionPattern support
   - Add bounding rectangle
   - Critical for validating app behavior

### Phase 3: Interaction Completeness (MEDIUM)
*Goal: Claude can interact with all common WinForms controls.*

6. **`select_item`** -- Combo box / list box support. ~1 hour.
7. **`click_menu_item`** -- Menu navigation. ~1.5 hours.
8. **`scroll_element`** -- Scroll support. ~1 hour.

### Phase 4: Specialized Controls (LOW-MEDIUM)
*Goal: Claude can work with complex data-heavy controls.*

9. **`get_grid_data`** -- DataGridView support. ~2 hours.
10. **`set_grid_cell`** -- DataGridView editing. ~1 hour.
11. **`handle_file_dialog`** -- File dialog automation. ~1.5 hours.

### Phase 5: Project Lifecycle (NICE-TO-HAVE)
*Goal: Claude can manage the full project lifecycle.*

12. **Document project scaffolding in CLAUDE.md** -- Guide Claude to use `dotnet new winforms` etc. ~30 min.
13. **Implement `listen_for_event`** -- Async event observation. ~3 hours.

---

## 5. Architecture Considerations

### Where new tools fit

All new tools follow the existing architecture pattern:
- Tool handler registered in `AutomationServer._tools` dictionary
- Handler method in `AutomationServer` class
- Heavy logic delegated to `AutomationHelper` (or a new helper class for grid operations)
- Results returned as `JsonElement` via `JsonDocument.Parse()`
- Image results use the existing `imageBase64` response pathway (lines 269-289)

### Image response pathway

The server already has a clean mechanism at lines 269-289 of Program.cs: if the tool result JSON contains an `imageBase64` property, it wraps it in an MCP `image` content block with `mimeType: image/png`. The fixes for `take_screenshot` and `render_form` just need to use this existing pathway.

### Session/element caching

`get_element_tree` should cache every discovered element in `SessionManager` so that Claude can immediately use the returned `elementId` values with `click_element`, `get_property`, etc. This is consistent with how `find_element` already works.

### Stderr capture for launched processes

`launch_app` currently uses `ProcessStartInfo` with `UseShellExecute = false` but does not redirect stdout/stderr. To support `get_process_status` returning stderr:
- Set `RedirectStandardError = true` in `LaunchApp`
- Read stderr asynchronously into a `StringBuilder` stored in the session
- `get_process_status` reads from this buffer

### Response size limits

`get_element_tree` with deep recursion could produce massive JSON. Mitigations:
- Default depth of 3, max of 10
- Default maxElements of 50, absolute cap of 200
- Truncation warning in response if cap hit
- Only include essential properties (skip bounding rect if tree is large)

### Thread safety

The existing `_lock` in AutomationHelper protects the process dictionary. New tools that read from the session should be safe since MCP processes requests sequentially (single stdio reader loop). No additional locking needed unless future work adds concurrency.

### Backward compatibility

- `take_screenshot`: making `outputPath` optional is a breaking change for callers expecting it. Mitigation: default to a temp file path if not provided, still save to disk, but always return base64.
- `render_form`: same approach. Keep `outputPath` as optional, always return base64.
- `get_property`: purely additive -- new property names, existing ones unchanged.

### Testing implications

The project requires 100% code coverage. Each new tool and enhancement needs:
- Unit tests for the helper methods (AutomationHelper additions)
- Integration test infrastructure for FlaUI-based tests (using TestApp)
- COVERAGE_EXCEPTION comments for platform-specific code that cannot run in CI

### File structure for new code

```
src/Rhombus.WinFormsMcp.Server/
  Program.cs                          -- add tool registrations and handlers
  Automation/
    AutomationHelper.cs               -- add GetElementTree, expanded GetProperty
    IAutomationHelper.cs              -- add new interface methods
    GridHelper.cs (new, Phase 4)      -- DataGridView-specific automation
    MenuHelper.cs (new, Phase 3)      -- Menu navigation logic
tests/Rhombus.WinFormsMcp.Tests/
    ElementTreeTests.cs (new)
    ProcessStatusTests.cs (new)
    ExpandedPropertyTests.cs (new)
```
