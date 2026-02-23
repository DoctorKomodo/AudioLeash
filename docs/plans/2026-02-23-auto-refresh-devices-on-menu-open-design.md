# Design: Auto-refresh device list on menu open

**Date:** 2026-02-23

## Problem

The tray context menu device list only refreshes when the user left-clicks the tray icon (which calls `RefreshDeviceList()` before showing the menu). On right-click, WinForms shows the `ContextMenuStrip` automatically without going through the mouse handler, so the device list may be stale. A "Refresh List" menu item exists as a workaround, but it closes the menu and requires the user to reopen it — poor usability.

## Solution

Hook `ContextMenuStrip.Opening`, which fires on the UI thread just before the menu becomes visible for both left- and right-click. Call `RefreshDeviceList()` there instead of in the mouse click handler.

## Changes (all in `AudioLeashContext.cs`)

1. Subscribe to `_contextMenu.Opening` in the constructor:
   ```csharp
   _contextMenu.Opening += (_, _) => RefreshDeviceList();
   ```

2. Remove the redundant explicit `RefreshDeviceList()` call from `TrayIcon_MouseClick`.

3. Remove the "Refresh List" `ToolStripMenuItem` and its associated separator from `RefreshDeviceList()`.

## Rejected alternatives

- **Right-click mouse handler:** Timing is unreliable — the menu appears before the mouse event fires on right-click.
- **Background timer:** Wasteful; adds complexity and race conditions for no real gain.

## Impact

- No new files or types.
- No behaviour change beyond the fix.
- Existing tests unaffected.
