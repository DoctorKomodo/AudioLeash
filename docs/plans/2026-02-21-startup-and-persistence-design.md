# Design: Windows Startup Toggle + Settings Persistence

**Date:** 2026-02-21
**Status:** Approved

---

## Overview

Add two related features to AudioLeash:

1. **"Start with Windows" menu toggle** — writes/removes a registry `Run` key so the app launches automatically at user login.
2. **Settings persistence** — saves and restores the user-selected audio device across restarts using a JSON file, making the startup feature actually useful.

---

## New Files

| File | Responsibility |
|---|---|
| `AudioLeash/SettingsService.cs` | JSON persistence in `%AppData%\AudioLeash\settings.json` |
| `AudioLeash/StartupService.cs` | Registry `HKCU\...\Run` key read/write |

Existing files modified: `AudioLeashContext.cs` (menu wiring + startup load).

---

## `SettingsService`

**Storage path:** `%AppData%\AudioLeash\settings.json`

**Schema:**
```json
{
  "selectedDeviceId": "<device-id or null>"
}
```

**API:**
- `string? LoadSelectedDeviceId()` — returns `null` if file missing, key absent, or JSON invalid
- `void SaveSelectedDeviceId(string? id)` — writes file; `null` clears the field

Uses `System.Text.Json` (BCL, no new package).

---

## `StartupService`

**Registry path:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
**Key name:** `AudioLeash`
**Value:** full path to the running executable (`Environment.ProcessPath`)

**API:**
- `bool IsEnabled { get; }` — `true` if the registry value exists
- `void Enable(string exePath)` — writes the value
- `void Disable()` — deletes the value

Constructor accepts an optional registry key path override for testability.

---

## Menu Layout

```
[ ] Speaker (Realtek)
[✓] Headphones (USB)
    ─────────────────
    Clear Selection (disable auto-restore)
    Refresh List
    ─────────────────
[✓] Start with Windows
    ─────────────────
    Exit
```

- "Start with Windows" is a checkable `ToolStripMenuItem`.
- Checked state reflects the live registry value (read on every menu rebuild).
- Clicking toggles the key; no balloon tip (state is visually obvious).

---

## Startup Behavior

### On app launch
1. Call `SettingsService.LoadSelectedDeviceId()`.
2. If a saved ID is found **and** the device is currently active → seed `DeviceSelectionState` with it (auto-restore begins immediately).
3. Otherwise → seed from Windows current default (existing behavior); do not persist anything yet.

### On device selection
- After `_selection.SelectDevice(deviceId)` → also call `_settingsService.SaveSelectedDeviceId(deviceId)`.

### On "Clear Selection"
- After `_selection.ClearSelection()` → also call `_settingsService.SaveSelectedDeviceId(null)`.

### On graceful unavailable-device handling
- After `_selection.ClearSelection()` (in `OnDefaultDeviceChanged`) → also call `_settingsService.SaveSelectedDeviceId(null)`.

---

## Testing

### `SettingsServiceTests`
- Load from missing file → `null`
- Load after saving an ID → returns correct ID
- Save `null` → subsequent load returns `null`
- Load from corrupted JSON → `null` (graceful)

Tests use a temp directory path injected via constructor.

### `StartupServiceTests`
- `IsEnabled` returns `false` when key absent
- `Enable()` → `IsEnabled` returns `true`
- `Disable()` after `Enable()` → `IsEnabled` returns `false`

Tests use a test-specific registry subkey injected via constructor to avoid touching the real `Run` key.

---

## Out of Scope

The following are explicitly deferred:
- Saving/restoring any other settings (e.g. window position, theme)
- Startup with elevated privileges (Task Scheduler path)
- Communications device or recording device persistence
