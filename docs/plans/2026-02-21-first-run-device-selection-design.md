# Design: Fix First-Run Device Selection Behavior

**Date:** 2026-02-21
**Status:** Approved

## Problem

On startup, if no device is saved in settings, AudioLeash silently seeds `_selection.SelectedDeviceId` from the current Windows default audio device. This selection is not persisted, and the user never explicitly chose it. The result is that auto-restore is active for a device the user never consented to leash.

Additionally, if a previously saved device is unavailable at startup, the same fallback fires — again without user consent or notification.

## Goal

- **First run** (no settings file): start with no device selected; notify the user they need to pick one.
- **Saved device unavailable at startup**: clear the selection, persist the cleared state, and notify the user their saved device was not found.
- **Saved device available at startup**: unchanged behavior — restore and leash as before.

## Design

### Change: `AudioLeashContext` constructor

Remove the Windows-default fallback (current lines 67–73):

```csharp
// REMOVE THIS BLOCK:
if (_selection.SelectedDeviceId is null)
{
    using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    if (defaultDevice is not null)
        _selection.SelectDevice(defaultDevice.ID);
}
```

Replace with two explicit paths after the saved-device check:

1. **No settings file** (`savedId is null`): show a balloon tip welcoming the user and prompting them to select a device.
2. **Saved device unavailable** (`savedId is not null` but not in the active device list): call `_settingsService.SaveSelectedDeviceId(null)`, clear the in-memory selection, and show a balloon tip informing the user their saved device was not found.

### Balloon tip messages

| Scenario | Title | Text | Icon |
|---|---|---|---|
| First run | "Welcome to AudioLeash" | "Click here to select a device and enable auto-restore." | Info |
| Saved device unavailable at startup | "Saved Device Not Found" | "Your saved audio device was not found. Select a device from the tray menu to re-enable auto-restore." | Info |

### No other changes

- `DeviceSelectionState`, `SettingsService`, `PolicyConfigClient`, `Program.cs` — unchanged.
- `RefreshDeviceList` already handles the no-selection state correctly (no checkmark, "Clear Selection" disabled).
- `DeviceMenuItem_Click` already persists the device ID when the user picks explicitly.

## Testing

- Unit tests for `DeviceSelectionState` are unaffected (the removed code is in `AudioLeashContext`, not the pure state machine).
- No new unit tests are required for this change — the behavior is constructor initialization logic tied to WinForms and the audio stack, which the test strategy explicitly excludes.
- Manual verification: first launch (no settings file), launch with settings pointing to a disconnected device, launch with settings pointing to a connected device.
