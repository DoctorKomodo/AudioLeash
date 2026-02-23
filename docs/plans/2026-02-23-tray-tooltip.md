# Tray Tooltip Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Show the currently selected audio device name in the system tray icon tooltip on hover.

**Architecture:** Add a `UpdateTrayTooltip(string? deviceName)` helper to `AudioLeashContext` and call it at the four places where selection state changes (device selected, device auto-restored, selection cleared, startup restore). No changes to `DeviceSelectionState` or any other file.

**Tech Stack:** C# 12 / .NET 10, WinForms `NotifyIcon`

---

### Task 1: Create feature branch

**Files:**
- No file changes — git only

**Step 1: Create and switch to feature branch**

```bash
git checkout -b feature/tray-tooltip
```

Expected: `Switched to a new branch 'feature/tray-tooltip'`

---

### Task 2: Add `UpdateTrayTooltip` helper and wire up all call sites

No new unit tests are needed — the tooltip update is a pure UI side-effect with no branching logic beyond the truncation guard, which is covered by manual verification below.

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs`

**Step 1: Add the helper method**

In `AudioLeashContext.cs`, add this private method after `ShowError` (around line 384):

```csharp
private void UpdateTrayTooltip(string? deviceName)
{
    string text = deviceName is null
        ? "AudioLeash — No device selected"
        : $"AudioLeash — {deviceName}";
    _trayIcon.Text = text.Length > 63 ? text[..62] + "…" : text;
}
```

> `NotifyIcon.Text` throws `ArgumentException` if the value exceeds 64 characters (including the null terminator WinForms passes to Win32). Capping at 63 printable chars is safe.

**Step 2: Call it at startup (constructor)**

Locate the block in the constructor that calls `_selection.SelectDevice(savedId)` (around line 93). The device name is available from the `active` list that was already iterated to check availability. Update the block:

```csharp
if (available)
{
    string? selectedName = active.FirstOrDefault(d => d.ID == savedId)?.FriendlyName;
    _selection.SelectDevice(savedId);
    UpdateTrayTooltip(selectedName);
}
```

The `else` branch calls `_settingsService.SaveSelectedDeviceId(null)` — no tooltip call needed there because the constructor has not yet set a device, so the default `"AudioLeash"` initial value is fine until a balloon tip is shown. Actually — add a tooltip update there too so the state is consistent:

```csharp
else
{
    _settingsService.SaveSelectedDeviceId(null);
    UpdateTrayTooltip(null);
    _trayIcon.ShowBalloonTip( ...existing code... );
}
```

**Step 3: Call it in `DeviceMenuItem_Click`**

`deviceName` is already a local variable. Add the call after `_selection.SelectDevice(deviceId)`:

```csharp
_selection.SelectDevice(deviceId);
UpdateTrayTooltip(deviceName);
_settingsService.SaveSelectedDeviceId(deviceId);
```

**Step 4: Call it in `ClearSelection_Click`**

After `_selection.ClearSelection()`:

```csharp
_selection.ClearSelection();
UpdateTrayTooltip(null);
_settingsService.SaveSelectedDeviceId(null);
```

**Step 5: Call it in `OnDefaultDeviceChanged` restore path**

`restoredName` is already a local variable inside the `SafeInvoke` lambda. Add the call after `_policyConfig.SetDefaultEndpoint(restoreId)` and before the balloon tip:

```csharp
_policyConfig.SetDefaultEndpoint(restoreId);

var restoreDevices = _enumerator
    .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
    .ToList();
string restoredName = restoreDevices.FirstOrDefault(d => d.ID == restoreId)?.FriendlyName ?? restoreId;
foreach (var d in restoreDevices) d.Dispose();

UpdateTrayTooltip(restoredName);

_trayIcon.ShowBalloonTip( ...existing code... );
```

**Step 6: Build and verify**

```bash
dotnet build AudioLeash.sln
```

Expected: `Build succeeded.` with 0 errors.

**Step 7: Run tests**

```bash
dotnet test AudioLeash.sln
```

Expected: All existing tests pass.

**Step 8: Manual smoke test**

1. Run the application.
2. Hover over the tray icon — tooltip should read `"AudioLeash — No device selected"` (or the saved device name if one was previously saved).
3. Select a device from the menu — tooltip should update to `"AudioLeash — {deviceName}"`.
4. Click "Clear Selection" — tooltip should revert to `"AudioLeash — No device selected"`.

**Step 9: Commit**

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "feat: show selected device name in tray icon tooltip"
```

---

### Task 3: Update README

**Files:**
- Modify: `README.md`

**Step 1: Mark the tooltip feature as implemented**

Find the future development section in `README.md` and remove or mark as done the line:

> Tray icon tooltip showing selected device name

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: mark tray tooltip feature as implemented"
```

---

### Task 4: Finish the branch

Once the work is reviewed and approved, merge to main following the project workflow:

```bash
git checkout main
git merge --no-ff feature/tray-tooltip
git push
git branch -d feature/tray-tooltip
```
