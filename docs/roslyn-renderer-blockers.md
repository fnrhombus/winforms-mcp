# Roslyn FormRenderer - Kitchen Sink Blockers

## Issue 1: Form properties set via ObjectCreation stored as fields instead of properties
**Status**: RESOLVED
**Impact**: CRITICAL - Form renders at wrong size, wrong font, wrong colors
**Details**: `this.ClientSize = new System.Drawing.Size(950, 580)` stores a Size in `_fields["ClientSize"]` 
instead of setting `_form.ClientSize`. Same for `this.Font`, `this.BackColor`, etc.
The ObjectCreation fast path in `ExecuteAssignment` always stores in `_fields` and returns early.
**Fix**: Check if the form has a writable property with that name before defaulting to field storage.
Also: when `GetFieldName` returns null (nested property like `this.nudLevel.Value = new decimal(...)`),
fall through to the property-setting code path instead of returning early.

## Issue 2: C# keyword types (int, float, etc.) not resolved
**Status**: RESOLVED
**Impact**: MEDIUM - `new decimal(new int[] { 42, 0, 0, 0 })` fails because `int` doesn't resolve
**Details**: NumericUpDown.Value/Maximum use designer pattern `new decimal(new int[] { ... })`.
ResolveType("int") returns null since it only tries namespace-qualified lookups.
**Fix**: Added KeywordTypes dictionary mapping C# keywords to System types.

## Issue 3: CheckedListBox Items.AddRange fails silently
**Status**: RESOLVED
**Impact**: HIGH - CheckedListBox items never rendered; BackColor also lost due to exception cascade
**Details**: `CheckedListBox` declares its own `Items` property (returning `CheckedListBox.ObjectCollection`)
that hides the inherited `ListBox.Items` property. `GetProperty("Items")` throws 
`AmbiguousMatchException`, which was swallowed by the renderer's catch-all exception handler.
This caused the entire statement to fail, and since BackColor was set before Items.AddRange,
it appeared as if both properties were lost.
**Fix**: Use `GetProperty("Items", BindingFlags.DeclaredOnly)` first to get the most-derived property,
falling back to general binding if DeclaredOnly finds nothing.

## Issue 4: Visual styles not enabled for headless rendering
**Status**: RESOLVED
**Impact**: LOW - Controls render with classic style instead of modern visual styles
**Fix**: Added `EnsureVisualStyles()` with static guard to call `Application.EnableVisualStyles()` once.
