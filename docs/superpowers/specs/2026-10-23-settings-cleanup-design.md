# Settings Cleanup: Design

**Date:** 2026-10-23
**Status:** Awaiting approval

## Intent

How AudioLeash stores its settings has drifted as features were added:

- **Two shapes for a saved device.** The ranked lists and the keep-alive list store each device as `{ id, name, description }`. The single saved pick (the legacy locked device, or the manual override in priority mode) is stored as two loose fields per flow, `selectedPlaybackDeviceId` and `selectedPlaybackDeviceName`, with no description.
- **Copy-pasted flows.** Playback and recording each have a twin of every load and save method.
- **Leftovers.** A field from before recording support (`selectedDeviceId`) is migrated on some save paths and not others. Five public methods are only called from tests.
- **Two copies of everything.** The live logic holds the ranked lists, picks and keep-alive list. `SettingsService` re-reads and patches the file one field at a time. The context also keeps its own copies of the pick names (`_savedPlaybackDeviceName`, `_savedCaptureDeviceName`). Every change updates these copies by hand, side by side, at about 20 call sites.

The cost showed up during the driver-update fix (v2.1.1). The pick needed a special case ("no description, match on name alone") because it was stored differently. The rebind code had to update the live state and the file separately for each kind of entry.

The user wants alike features stored and handled the same way, with **one canonical copy** of the settings instead of copies kept in step by hand.

## Goals

1. **One shape for a saved device.** The pick, ranked entries and keep-alive entries are all a `RankedDevice` (`Id`, `Name`, `Description`), in the file and in memory.
2. **One shape per flow.** Playback and recording are each one block, holding the pick and the ranked list, handled by the same code.
3. **One canonical copy.** While the app runs, the live state is the only copy of the settings. The file is a snapshot of it, rebuilt in full on every save. Nothing else holds settings.
4. **A versioned file**, so a future format change is detected for certain.
5. **Automatic upgrade** from the current v2.1.x file on first launch. The user keeps their ranking, keep-alive devices and pick.
6. **Dead code removed:** the unused methods, the per-field load and save methods, the name copies and the legacy `selectedDeviceId` handling.

## Non-goals

- No behaviour change the user can see: switching, restore, priority, keep-alive, balloons, the tray menu and the settings window all behave exactly as in v2.1.1. Anything else spotted along the way is written down, not fixed.
- No downgrade support. The user's decision: an older AudioLeash may fail to read the new file and start fresh.
- No upgrade from formats older than v2.1.x. The user's decision: converting from the current format is enough.
- No hand-editing support. The user's decision: the file is entirely app-managed.
- No new settings, and nothing from DEVELOPMENT.md's roadmap.

## Anti-goals (it fails if…)

- …after upgrading, the user has to set anything up again: ranking, keep-alive or the pick.
- …the live state and the file can disagree after a change, or any code path can change one without the other.
- …a missing, corrupt or unreadable file produces an error dialog or crash, instead of today's silent "start empty".
- …a file that exists but briefly can't be read gets overwritten with empty settings.
- …first-run detection changes. The welcome balloon still shows only when no settings file exists.
- …edits made in the settings window are lost when Windows logs off, or the installer closes the app, while the window is open. Today they survive.
- …a failed write blocks or breaks anything. Saving stays best-effort and silent, as today.

## The model

### Where the canonical copy lives

The live state objects already exist. They become the canonical copy:

| Setting | Lives in (canonical) |
|---|---|
| Playback / recording ranked list | `PriorityState.Ranked` (one per flow) |
| Manual override (priority mode) | `PriorityState`, now held as a `RankedDevice` |
| Locked device (legacy mode) | `DeviceSelectionState`, now held as a `RankedDevice` |
| Keep-alive devices | `KeepAliveState.Devices` (already `RankedDevice`) |

`PriorityState` and `DeviceSelectionState` hold only the pick's ID today. They now hold the whole pick, so the file can be built from them alone. They keep `OverrideId` and `SelectedDeviceId` as read-only shortcuts to the held pick's `Id`, so the many ID-based callers and the decision logic stay unchanged. Their methods for setting the pick take a `RankedDevice`. Their decisions (restore, suspend, settled outcomes, priority outcomes) do not change.

### The snapshot

A plain immutable record describes the whole file, for example:

```
SettingsSnapshot(FlowSnapshot Playback, FlowSnapshot Recording, IReadOnlyList<RankedDevice> KeepAlive)
FlowSnapshot(RankedDevice? Pick, IReadOnlyList<RankedDevice> Priority)
```

- **Building it.** The context builds a snapshot from the live state in one method. Each flow's pick follows the rule that already applies today: in priority mode (a non-empty ranked list) the pick is the manual override; otherwise it is the locked device. This rule is a small pure function with its own tests.
- **Saving.** `SaveSettings()` in the context builds the snapshot and hands it to `SettingsService.Save`. Every code path that changes any setting calls it once, after the change. Nothing writes partial settings.
- **Skipping no-op writes.** `SettingsService` remembers the last JSON it wrote, or the JSON it loaded, and skips the write when the new JSON is identical. Saving after every change then stays cheap. *(Designer's call.)*
- **`PlanPersistedPick` is removed.** What it decided now falls out of the live state:
  - entering priority mode already clears the locked device;
  - staying in priority mode keeps the override;
  - leaving priority mode already sets the current default as the locked device.

  Its tests move to the snapshot rule, and to the existing mode-change paths where they cover behaviour.

### Startup

1. `SettingsService.Load()` returns a snapshot. It converts a v2.1.x file if needed (see Upgrade) and also reports whether a settings file exists, for first-run detection.
2. The startup rebinds (`DeviceRebinder`) run on the loaded snapshot, before anything is handed to the live state. A pick is rebound like any other entry.
3. The snapshot is loaded into the live state: ranked lists, picks (as the override or the locked device, depending on mode), and keep-alive.
4. If the upgrade or a rebind changed anything, `SaveSettings()` runs once.

The rest of startup (the grace period, restoring the default, balloons) is unchanged.

### At runtime

- **Tray menu picks, Return to priority order, Clear selection, an override ending, keep-alive toggles, rebinds:** change the live state, then `SaveSettings()`.
- **Pick details.** A pick chosen from the tray menu records its description (`MMDevice.DeviceFriendlyName`) along with its name. So does a locked device set from the current default when leaving priority mode.
- **Filling in pick details.** *(Designer's call:)* at startup, a pick whose ID matches a known device takes that device's current name and description. The device list is already enumerated for the rebind, and the change is saved in step 4. Upgraded picks gain their description on the first launch where the device is present, so the name-only rebind rule fades out naturally. The settings window no longer refreshes the pick's name when it closes; the pick in the live state already carries one.
- **The tooltip and menu** read the pick's name from the live state instead of the removed `_saved*DeviceName` copies. They fall back exactly as today when the device itself can't be resolved.

### Settings window closing during logoff or an installer update

The user sees no change: edits made in the settings window survive, as today. Today this works by writing the edits straight to the file. In the new model the file is built only from the live state. So when the app is exiting, the window's edits are applied to the live state without switching any device or showing any balloon, and then `SaveSettings()` runs.

The exit path makes no audio-service calls, with one exception kept from today. If a flow leaves priority mode (its ranked list was emptied), the current Windows default becomes the locked device, and reading it is a COM call. *(Designer's call:)* that read stays best-effort, and the same lookup supplies the device's name and description. If it fails, the emptied list is still saved, with no locked device. Today a failure here loses that flow's edits entirely; the new model can't half-save, so this edge improves as a side effect.

## The file

```json
{
  "version": 2,
  "playback": {
    "pick": { "id": "…", "name": "…", "description": "…" },
    "priority": [ { "id": "…", "name": "…", "description": "…" } ]
  },
  "recording": {
    "pick": null,
    "priority": []
  },
  "keepAlive": [ { "id": "…", "name": "…", "description": "…" } ]
}
```

- `version` is `2`. The current file has no version and counts as version 1.
- Keep-alive stays top-level, because it applies to playback only.
- Property names stay camelCase. Still written to `%AppData%\AudioLeash\settings.json`.
- Load-time sanitizing stays as today for every list: drop null and empty IDs, keep the first of duplicate IDs, replace a missing name with "Unknown device". The same applies to a pick.

### Upgrade from the current file

A file with no `version` is read in the v2.1.x shape and converted:

| Current field | New field |
|---|---|
| `selectedPlaybackDeviceId` (falling back to `selectedDeviceId`) + `selectedPlaybackDeviceName` | `playback.pick` (description null) |
| `selectedCaptureDeviceId` + `selectedCaptureDeviceName` | `recording.pick` (description null) |
| `playbackPriority` | `playback.priority` |
| `capturePriority` | `recording.priority` |
| `keepAliveDevices` | `keepAlive` |

- The converted settings are saved in the new format during startup (step 4), so the conversion runs once.
- An upgraded pick has no description. `DeviceRebinder`'s rule that a null description matches on name alone therefore stays.
- A pick with an ID but no name gets "Unknown device". *(Designer's call:)* the one visible side effect is accepted. A nameless pick only comes from a damaged or very old file, so in practice it doesn't occur. If it did, its tooltip would read "Unknown device (waiting)" instead of "Unknown (waiting)".
- A file with a `version` higher than 2 is treated like a corrupt file.

### A file that exists but won't load

Loading once means a brief read failure (antivirus or a backup tool holding the file) could otherwise lead the first save to overwrite good settings with empty ones. *(Designer's call:)*

- A read that fails with an I/O error (locked, sharing violation) is retried a few times over about a second before giving up.
- If the file still can't be used (unreadable, invalid JSON, unknown version), the app starts empty, as today. Before its first write that session, it copies the existing file to `settings.json.bak`, overwriting any older backup. Nothing reads the backup automatically; it's a safety net only.
- *(Designer's call:)* if that copy fails too (a file that stays locked usually can't be copied either), the write is skipped, and the copy is retried on each later save. Until it succeeds, that session's changes aren't saved. That's the lesser loss: the alternative destroys good settings.

## Threading

All settings loads and saves run on the UI thread, as today. `PriorityState` already guards its fields with a lock because the audio thread reads them; holding the override as a `RankedDevice` stays under that lock. `DeviceSelectionState` gains no new cross-thread readers: the audio thread reads only `SelectedDeviceId`, which must stay safe to read from another thread. A single volatile reference to the held pick does this.

## Tests

- **`SettingsServiceTests`, rewritten:**
  - snapshot round trip;
  - missing file, corrupt file and an unknown future version all start empty;
  - first-run detection;
  - sanitizing, including the pick;
  - an identical save writes nothing;
  - a corrupt file is copied to `.bak` before the first write;
  - a good file and a missing file create no backup;
  - if the backup copy fails, nothing is written, and a later save retries it.
- **Upgrade tests** use a real v2.1.x file as the fixture (the shape of the user's current file):
  - every field lands in the right place;
  - a nameless pick gets the fallback name;
  - `selectedDeviceId` is used as the fallback for the playback pick;
  - the saved result is version 2 and reloads directly.
- **The snapshot pick rule:** priority mode saves the override, legacy mode saves the locked device, and an empty pick saves null.
- **Filling in pick details at startup:** a pick whose ID matches a known device takes its current name and description; one that doesn't match is left unchanged. This is a pure function with its own tests.
- **`DeviceSelectionStateTests` and `PriorityStateTests`** are updated for setting the pick as a `RankedDevice`. Their behaviour assertions don't change.
- All other existing tests pass unchanged.

## Left to the builder

- Exact type and method names, and whether the upgrade lives inside `SettingsService` or in a small separate pure class. The latter is preferred if it keeps the tests simpler.
- How the context's code paths are grouped so each change ends in one `SaveSettings()` call. The anti-goal still holds: no path may change a setting without saving it.
- The order of work, as long as each commit builds and the tests pass.
