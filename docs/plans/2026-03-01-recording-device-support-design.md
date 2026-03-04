# Recording Device (Microphone) Support — Design

## Summary

Extend AudioLeash to leash recording (capture) devices in addition to playback (render) devices. Users can independently lock a playback device and a recording device. When Windows changes either default, AudioLeash restores the user's choice.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Independence | Fully independent selections | User can lock playback only, recording only, or both |
| Clear Selection | Single action clears both | Simplicity; avoids cluttering menu |
| Menu layout | Two sections with headers | Clean separation, single menu, no extra clicks |
| Tooltip | Shows both locked devices | Full status at a glance |
| Architecture | Two `DeviceSelectionState` instances | YAGNI; reuse existing proven class without modification |

## Architecture

### State Management

`DeviceSelectionState` is unchanged. `AudioLeashContext` holds two instances:

- `_playbackState` — tracks the locked playback device
- `_captureState` — tracks the locked recording device

Each instance independently manages `SelectedDeviceId` and `IsInternalChange`.

### Settings Persistence

`SettingsService` evolves the data model from a single `SelectedDeviceId` to two fields:

```csharp
private sealed record AppSettings(
    string? SelectedPlaybackDeviceId,
    string? SelectedCaptureDeviceId);
```

**Backward compatibility:** On load, if the old `SelectedDeviceId` field is present and the new fields are absent, map it to `SelectedPlaybackDeviceId`.

The public API changes from a single `LoadSelectedDeviceId()`/`SaveSelectedDeviceId()` pair to separate methods for each flow, or parameterized by `DataFlow`.

### Notification Handling

`AudioNotificationClient.OnDefaultDeviceChanged` currently filters to `DataFlow.Render` + `Role.Multimedia`. Updated to also handle `DataFlow.Capture` + `Role.Multimedia`:

- Render notifications → `_playbackState.EvaluateDefaultChange()` → restore if needed
- Capture notifications → `_captureState.EvaluateDefaultChange()` → restore if needed

Both use the same `SafeInvoke` marshalling and `PolicyConfigClient.SetDefaultEndpoint` for restoration. `PolicyConfigClient` is already device-type-agnostic (takes a device ID string, sets all three roles).

### Menu Structure

```
┌───────────────────────────────┐
│  Playback                     │  ← Bold header (disabled item)
│  ───────────────────────────  │
│  ✔ Speakers (Realtek)         │  ← Checked = selected
│    HDMI Output                │
│    USB Headset                │
│                               │
│  Recording                    │  ← Bold header (disabled item)
│  ───────────────────────────  │
│  ✔ Built-in Mic               │  ← Checked = selected
│    USB Headset Mic            │
│                               │
│  ───────────────────────────  │
│    Clear Selection            │  ← Clears BOTH playback and recording
│  ☐ Start with Windows         │
│    Exit                       │
└───────────────────────────────┘
```

Device enumeration calls `EnumerateAudioEndPoints` twice: once with `DataFlow.Render`, once with `DataFlow.Capture`.

Auto-refresh on menu open applies to both sections.

### Tooltip

Shows both locked devices when set:

```
AudioLeash
Playback: Speakers (Realtek)
Recording: Built-in Mic
```

Omits lines for flows with no selection. Falls back to just "AudioLeash" when neither is locked.

### Error Handling

Same as existing playback logic:
- If a locked device becomes unavailable (unplugged), clear that selection and show a balloon notification
- If device restoration fails, log and continue
- Each flow handles errors independently

## Testing

- `DeviceSelectionState` tests remain unchanged (class is unmodified)
- `SettingsService` tests extended for:
  - Two-field persistence (playback + capture)
  - Backward-compatible migration from old single-field format
  - Independent save/load of each field
- No new integration tests — composition is in UI-bound `AudioLeashContext`

## Files Changed

| File | Change |
|---|---|
| `SettingsService.cs` | New data model with two device IDs; backward-compatible load; new save/load API |
| `AudioLeashContext.cs` | Second state machine instance; capture device enumeration; menu sections; tooltip; notification routing |
| `SettingsServiceTests.cs` | Tests for new two-field model and migration |
| `README.md` | Update features list; remove "Recording device support" from future ideas |
