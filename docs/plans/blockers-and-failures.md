# Blockers & Failures Log

Tracking issues encountered during implementation of the MCP gap analysis plan.

## Phase 0 — Critical Bug Fix
- **Status:** COMPLETE
- **Note:** The 7 hidden tool definitions were already added by the parallel headless-mode Claude instance. Only the test assertion needed updating.

## Phase 1 — Reliability & Async
### 1.1 wait_for_condition — COMPLETE
### 1.2 toggle_element — COMPLETE

## Phase 2 — Data-Heavy Apps
### 2.1 scroll_element — COMPLETE
### 2.2 get_table_data — COMPLETE
- **Issue (attempt 1):** `GridHeader.Cells` doesn't exist — FlaUI uses `GridHeader.Columns`. Fixed on second attempt.
### 2.3 set_table_cell — COMPLETE

## Phase 3 — Window & Multi-Form Management
### 3.1 manage_window — COMPLETE
### 3.2 list_windows — COMPLETE
### 3.3 get_focused_element — COMPLETE

## Phase 4 — Event System & Context Menus
### 4.1 raise_event — COMPLETE
### 4.2 listen_for_event — COMPLETE
- **Issue (attempt 1):** FlaUI's `RegisterStructureChangedEvent` delegate takes 3 args (element, changeType, runtimeIds), not 2. Also `UnregisterFocusChangedEvent` requires the handler reference.
- **Decision:** Replaced UIA event handler approach with polling-based detection. More reliable and avoids COM threading issues. `focus_changed` polls `FocusedElement()`, `structure_changed` polls child count. `property_changed` redirects to `wait_for_condition`.
### 4.3 open_context_menu — COMPLETE

## Phase 5 — Polish & Edge Cases
### 5.1 get_clipboard / set_clipboard — COMPLETE
### 5.2 read_tooltip — COMPLETE
### 5.3 find_elements — COMPLETE

---

## Failure Log

| Phase | Task | Attempt | Issue | Resolution |
|-------|------|---------|-------|------------|
| 2 | get_table_data | 1 | `GridHeader.Cells` doesn't exist | Changed to `GridHeader.Columns` |
| 4 | listen_for_event | 1 | StructureChanged delegate signature mismatch + UnregisterFocusChangedEvent API | Switched to polling-based approach |

## Final Stats
- **Total tools:** 33 (from 12 visible at start)
- **All tests passing:** 230/230
- **Build errors encountered:** 3 (all resolved in 1-2 attempts)
- **Tasks requiring research/giving up:** 0
