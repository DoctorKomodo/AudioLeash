# First-Run Device Selection Fix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove the silent auto-selection of the Windows default device on startup; instead show a balloon tip guiding the user to pick a device explicitly.

**Architecture:** The entire change is in the `AudioLeashContext` constructor. The auto-seed fallback (lines 67–73) is replaced with two explicit notification paths: one for first run (no settings file), one for saved device unavailable at startup.

**Tech Stack:** C# 12 / .NET 8, WinForms (`NotifyIcon.ShowBalloonTip`), NAudio.Wasapi

---

### Task 1: Create feature branch

**Files:** none

**Step 1: Create and check out the feature branch**

```bash
git checkout -b feature/fix-first-run-device-selection
```

Expected: `Switched to a new branch 'feature/fix-first-run-device-selection'`

---

### Task 2: Fix the constructor in `AudioLeashContext.cs`

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs:53-75`

**Step 1: Read the current constructor** to confirm the exact lines before editing.

Open `AudioLeash/AudioLeashContext.cs` and locate the startup initialization block (roughly lines 53–75):

```csharp
// Try to restore the previously selected device; fall back to Windows current default.
string? savedId = _settingsService.LoadSelectedDeviceId();
if (savedId is not null)
{
    var active = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
    bool available = active.Any(d => d.ID == savedId);
    foreach (var d in active) d.Dispose();

    if (available)
    {
        _selection.SelectDevice(savedId);
    }
}

if (_selection.SelectedDeviceId is null)
{
    // No saved preference (or device unavailable) -- seed from Windows current default.
    using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    if (defaultDevice is not null)
        _selection.SelectDevice(defaultDevice.ID);
}

RefreshDeviceList();
```

**Step 2: Replace the block with the new behavior**

Replace the entire block above with:

```csharp
// Restore the previously selected device, or guide the user to pick one.
string? savedId = _settingsService.LoadSelectedDeviceId();

if (savedId is null)
{
    // First run: no settings file exists yet. Prompt the user to pick a device.
    _trayIcon.ShowBalloonTip(
        timeout:  4000,
        tipTitle: "Welcome to AudioLeash",
        tipText:  "Click the tray icon and select a device to enable auto-restore.",
        tipIcon:  ToolTipIcon.Info);
}
else
{
    var active = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
    bool available = active.Any(d => d.ID == savedId);
    foreach (var d in active) d.Dispose();

    if (available)
    {
        _selection.SelectDevice(savedId);
    }
    else
    {
        // Saved device is not connected. Clear persisted selection and notify the user.
        _settingsService.SaveSelectedDeviceId(null);
        _trayIcon.ShowBalloonTip(
            timeout:  4000,
            tipTitle: "Saved Device Not Found",
            tipText:  "Your saved audio device was not found. Select a device from the tray menu to re-enable auto-restore.",
            tipIcon:  ToolTipIcon.Info);
    }
}

RefreshDeviceList();
```

**Step 3: Build the solution to confirm no compile errors**

```bash
dotnet build AudioLeash.sln
```

Expected: `Build succeeded.` with 0 errors.

**Step 4: Run existing tests to confirm nothing is broken**

```bash
dotnet test AudioLeash.sln
```

Expected: All tests pass (no failures).

**Step 5: Commit**

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "fix: remove auto-seed of Windows default device on startup

On first run (no settings file) AudioLeash now shows a balloon tip
prompting the user to select a device explicitly instead of silently
leashing the current Windows default without user consent.

When a previously saved device is unavailable at startup, the persisted
selection is cleared and a balloon tip informs the user."
```

---

### Task 3: Manual verification checklist

Perform these three manual tests before requesting a review. The app can only be run on Windows.

**Test A — First run**
1. Delete `%AppData%\AudioLeash\settings.json` (if it exists).
2. Build and run the app: `dotnet run --project AudioLeash/AudioLeash.csproj`
3. Expected: balloon tip "Welcome to AudioLeash — Click the tray icon and select a device to enable auto-restore."
4. Open the tray menu: no device should have a checkmark.
5. Select a device: checkmark appears, balloon tip "Audio Device Selected" fires, settings file is created.

**Test B — Saved device unavailable**
1. Ensure `settings.json` exists with a device ID that is currently NOT active (e.g. use a fake ID like `{00000000-0000-0000-0000-000000000000}`, or unplug the device before launching).
2. Run the app.
3. Expected: balloon tip "Saved Device Not Found".
4. Open tray menu: no device has a checkmark.
5. Inspect `settings.json`: `selectedDeviceId` should now be `null`.

**Test C — Normal startup (saved device available)**
1. Select a device normally (this saves its ID).
2. Exit and relaunch the app.
3. Expected: no balloon tip. The previously selected device has a checkmark in the tray menu.

---

### Task 4: Update README

**Files:**
- Modify: `README.md`

Find the section describing how AudioLeash works / first-run behavior (or the feature list) and add a note that on first launch the user is prompted to select a device from the tray menu. Keep it brief — one sentence in the appropriate section is enough.

Commit:

```bash
git add README.md
git commit -m "docs: update README to describe first-run device selection prompt"
```

---

### Task 5: Request code review

After all tests pass and README is updated, follow the project code review workflow:

1. Run a big-picture subagent review (how the change is used, implications).
2. Run a standard subagent review.
3. Address any findings.
4. Ask the user to approve merging to `main`.
