# Design: Tray Icon Tooltip Showing Selected Device Name

**Date:** 2026-02-23
**Status:** Approved

## Summary

Update the `NotifyIcon.Text` property so that hovering over the AudioLeash tray icon shows the currently selected audio device name instead of the static string `"AudioLeash"`.

## Tooltip Text Format

| State | Tooltip text |
|---|---|
| Device selected | `"AudioLeash — {deviceName}"` |
| No device selected | `"AudioLeash — No device selected"` |

`NotifyIcon.Text` is capped at 64 characters (WinForms throws `ArgumentException` if exceeded). Text longer than 63 characters is truncated to 62 chars with a trailing `…`.

## Implementation

### New helper method in `AudioLeashContext`

```csharp
private void UpdateTrayTooltip(string? deviceName)
{
    string text = deviceName is null
        ? "AudioLeash — No device selected"
        : $"AudioLeash — {deviceName}";
    _trayIcon.Text = text.Length > 63 ? text[..62] + "…" : text;
}
```

### Call sites

| Location | Trigger | Name source |
|---|---|---|
| Constructor (startup) | After `_selection.SelectDevice(savedId)` | Lookup in already-iterated `active` list |
| `DeviceMenuItem_Click` | After `_selection.SelectDevice(deviceId)` | `deviceName` local var |
| `ClearSelection_Click` | After `_selection.ClearSelection()` | `null` |
| `OnDefaultDeviceChanged` restore path | After `_policyConfig.SetDefaultEndpoint` | `restoredName` local var |

The existing `Text = "AudioLeash"` in the `NotifyIcon` initialiser remains as a safe initial value; `UpdateTrayTooltip` overwrites it during startup once the saved device is resolved.

## Testing

No new unit tests required. The tooltip update is a pure UI side-effect with no branching logic beyond the truncation guard, which is verified manually. `DeviceSelectionState` is unchanged.

## Files Changed

- `AudioLeash/AudioLeashContext.cs` — add `UpdateTrayTooltip`, call at four sites
