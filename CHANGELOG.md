# Changelog

All notable changes to FlaUI-MCP will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `windows_dump_ids` diagnostic tool for discovering stable AutomationIds (#35). Walks descendants of a window or subtree and emits a compact `AutomationId | ControlType | Name | Rect` table. Optional regex filter on AutomationId, optional `includeEmptyIds` to also list controls without an AutomationId. Use this when the regular snapshot is too noisy and you just want a focused list of identifiable controls.
- `windows_get_value` tool for reading programmatic element values (Value pattern, Toggle state, SelectionItem, RangeValue)
- `windows_snapshot` now supports `backend` parameter (`uia3` or `uia2`) for UIA2 fallback on WinForms controls
- **Integration test framework** with purpose-built WinForms (.NET Framework 4.8.1) and WPF (.NET 8) test applications
  - WinFormsTestApp: buttons, forms, 50-row DataGridView, TreeView, dialog launchers
  - WpfTestApp: equivalent WPF controls for cross-framework validation
  - 13 integration tests covering snapshot, click, and text tools
  - Shared test fixture for stable window handles across test runs
- `windows_press_key` tool for sending keyboard input (individual keys and modifier combinations)
- `windows_find` tool for targeted element discovery by name, automationId, role, or UIA pattern
- `windows_table` tool for reading DataGridView and table data as markdown tables
- `windows_wait` tool for polling UI elements until they reach a target state (exists, gone, enabled, focused)

### Changed
- **`windows_snapshot` now annotates each line with the element's AutomationId** when present (#36). The new format is `[ref=<refid>, id=<automationId>]` instead of just `[ref=<refid>]`. AutomationId is the stable identifier test code should target — Name is derived from many sources and can differ between out-of-process UIA (what the MCP sees) and in-process UIA (what test code at runtime sees), so surfacing both lets the agent make an informed choice. The previous fallback that displayed `[automationId]` as a fake Name is removed (it's now redundant with the ref annotation).
- **`windows_click` and `windows_invoke` are now separate tools** with 1:1 mappings to FlaUI primitives. Previously a single `windows_click` tool fused two incompatible code paths (UIA pattern dispatch and physical mouse click) behind a `method` parameter, which made it hard for callers to know which FlaUI primitive their tool call corresponded to. The split:
  - `windows_click` → physical mouse click. Maps to `Mouse.Click(element.GetClickablePoint(), button)`. Parameters: `ref`, `button`, `doubleClick`, `modifiers`. Brings the target window to the foreground first. Auto-promotes single clicks on WinForms DataGridView checkboxes to double-clicks (a real WinForms quirk — single click only enters edit mode).
  - `windows_invoke` → UIA pattern dispatch. Maps to `element.Patterns.Invoke|Toggle|SelectionItem.Pattern.<action>()`, in priority order. Parameters: `ref` only. Programmatic — no cursor movement, no mouse events, no focus required. Result names which pattern fired (e.g. `UIA Toggle: toggled X to On`).
  - The `method` parameter on `windows_click` is removed.
- `windows_click` now accepts a `modifiers` array parameter (`ctrl`, `shift`, `alt`, `win`) to hold keys during the click — enables Ctrl-click multi-select and Shift-click range-select in grids/lists (#34).
- `windows_snapshot` now limits row expansion for table, grid, and list elements to prevent oversized snapshots (configurable via `maxTableRows` parameter, default 5)

### Fixed
- `windows_find` refreshes window reference to avoid stale cached element properties
- Column header detection for WinForms DataGridView controls
- `windows_wait` with `state='gone'` enumerates fresh top-level windows to avoid stale desktop cache
- **`windows_click` no longer hangs the MCP worker thread when the target opens a modal dialog** (#32). The Invoke pattern call is now dispatched on a background thread so `windows_click` returns as soon as the click is dispatched, matching how `Mouse.Click()` already behaves. Previously, clicking any button whose handler called `ShowDialog()` would block the single MCP worker thread until the dialog was dismissed, causing every subsequent tool call to time out and forcing the user to kill the application under test.

## [0.1.0] - 2024-02-02

### Added
- Initial release
- **Core MCP Tools:**
  - `windows_launch` - Launch Windows applications
  - `windows_snapshot` - Capture accessibility tree with element refs
  - `windows_click` - Click elements by ref (uses Invoke pattern when available)
  - `windows_type` - Type text into elements
  - `windows_fill` - Clear and fill text fields
  - `windows_get_text` - Get element text content
  - `windows_screenshot` - Capture window/element screenshots
  - `windows_list_windows` - List all open windows
  - `windows_focus` - Bring window to foreground
  - `windows_close` - Close windows
  - `windows_batch` - Execute multiple actions in a single call

- **Architecture:**
  - MCP protocol handler (JSON-RPC over stdio)
  - Element registry for ref ↔ AutomationElement mapping
  - Snapshot builder for agent-friendly accessibility tree format
  - Session manager for tracking launched applications

- **Documentation:**
  - README with installation and usage instructions
  - GitHub Actions for CI/CD
  - MIT License

### Technical Details
- Built on [FlaUI](https://github.com/FlaUI/FlaUI) for Windows UI Automation
- Uses UIA3 for modern app support (WPF, UWP, Win32)
- Targets .NET 8.0-windows
- Prefers control patterns (Invoke, Value, Toggle) over mouse simulation
