# Auto-refresh Device List on Menu Open Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refresh the audio device list every time the tray context menu opens, eliminating the need for a manual "Refresh List" menu item.

**Architecture:** Subscribe to `ContextMenuStrip.Opening` (fires on the UI thread just before the menu is shown, for both left- and right-click). Remove the now-redundant explicit call in the left-click handler and remove the "Refresh List" menu item.

**Tech Stack:** C# 14, .NET 10, WinForms (`ContextMenuStrip.Opening` event)

---

### Task 1: Wire up `ContextMenuStrip.Opening`

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs`

**Step 1: Add the `Opening` subscription in the constructor**

In `AudioLeashContext.cs`, locate the line:
```csharp
_ = _contextMenu.Handle;
```
Add immediately after it:
```csharp
_contextMenu.Opening += (_, _) => RefreshDeviceList();
```

**Step 2: Remove the redundant call from `TrayIcon_MouseClick`**

The method currently looks like:
```csharp
private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
{
    if (e.Button != MouseButtons.Left) return;

    RefreshDeviceList();

    // WinForms only shows the context menu automatically on right-click;
    // invoke it manually for left-click via reflection.
    typeof(NotifyIcon)
        .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.Invoke(_trayIcon, null);
}
```
Remove the `RefreshDeviceList();` line. The `Opening` event will fire when `ShowContextMenu` causes the menu to open, so the refresh still happens — just via the event now.

Result:
```csharp
private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
{
    if (e.Button != MouseButtons.Left) return;

    // WinForms only shows the context menu automatically on right-click;
    // invoke it manually for left-click via reflection.
    typeof(NotifyIcon)
        .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.Invoke(_trayIcon, null);
}
```

**Step 3: Remove the "Refresh List" menu item from `RefreshDeviceList`**

In `RefreshDeviceList()`, remove these three lines:
```csharp
var refreshItem = new ToolStripMenuItem("Refresh List");
refreshItem.Click += (_, _) => RefreshDeviceList();
_contextMenu.Items.Add(refreshItem);
```

Also remove the separator that sits immediately before "Refresh List". That block looks like:
```csharp
_contextMenu.Items.Add(new ToolStripSeparator());

var clearItem = new ToolStripMenuItem("Clear Selection  (disable auto-restore)")
{
    Enabled = _selection.SelectedDeviceId is not null,
};
clearItem.Click += ClearSelection_Click;
_contextMenu.Items.Add(clearItem);

var refreshItem = new ToolStripMenuItem("Refresh List");
refreshItem.Click += (_, _) => RefreshDeviceList();
_contextMenu.Items.Add(refreshItem);

_contextMenu.Items.Add(new ToolStripSeparator());
```

After removing the refresh item, it should look like:
```csharp
_contextMenu.Items.Add(new ToolStripSeparator());

var clearItem = new ToolStripMenuItem("Clear Selection  (disable auto-restore)")
{
    Enabled = _selection.SelectedDeviceId is not null,
};
clearItem.Click += ClearSelection_Click;
_contextMenu.Items.Add(clearItem);

_contextMenu.Items.Add(new ToolStripSeparator());
```

**Step 4: Build to verify no compile errors**

```bash
dotnet build AudioLeash.sln
```
Expected: `Build succeeded` with 0 errors.

**Step 5: Run tests**

```bash
dotnet test AudioLeash.sln
```
Expected: all tests pass (no tests cover this WinForms wiring directly, but ensure nothing is broken).

**Step 6: Commit**

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "feat: auto-refresh device list when tray menu opens"
```

---

### Task 2: Update README

**Files:**
- Modify: `README.md`

**Step 1: Find the feature list / future ideas section**

Look for any mention of "Refresh List" or tray menu behaviour in `README.md` and update accordingly. Remove any reference to the manual refresh menu item if it is documented as a feature.

Also check the "Future Development Areas" section in `CLAUDE.md` — if "Tray icon tooltip showing selected device name" or a similar item was listed, leave it. Remove only references to the manual refresh item.

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: remove reference to manual Refresh List menu item"
```
(Skip this task entirely if README has no mention of the refresh item.)
