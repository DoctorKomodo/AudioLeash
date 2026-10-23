# Settings Cleanup: Design

**Date:** 2026-10-23
**Status:** Awaiting approval

## Intent

How AudioLeash stores its settings has drifted as features were added. The ranked lists and the keep-alive list store each device as `{ id, name, description }`. The single saved pick (the legacy locked device, or the manual override in priority mode) is stored as two loose fields per flow, `selectedPlaybackDeviceId` and `selectedPlaybackDeviceName`, with no description. Playback and recording each have a copy-pasted twin of every load and save method. A field left over from before recording support (`selectedDeviceId`) is migrated on some save paths and not others. Five public methods are only called from tests. The app also keeps its own copies of the saved pick names (`_savedPlaybackDeviceName`, `_savedCaptureDeviceName`) and has to update them by hand next to every save.

The cost showed up during the driver-update fix (v2.1.1): the pick needed a special case ("no description, match on name alone") because it was stored differently from the ranked entries.

The user wants alike features stored and handled the same way, so future fixes apply everywhere without special cases.

## Goals

1. **One shape for a saved device.** The pick, ranked entries and keep-alive entries are all a `RankedDevice` (`Id`, `Name`, `Description`).
2. **One shape per flow.** Playback and recording are each one block holding the pick and the ranked list, and are handled by the same code.
3. **A versioned file**, so a future format change is detected for certain rather than guessed from missing fields.
4. **Automatic upgrade** from the current v2.1.x file on first launch, so the user keeps their ranking, keep-alive devices and pick.
5. **One owner of saved state.** The app stops keeping its own copies of the saved pick names and reads them from the settings object. *(Designer's call:)* this covers the names only. `PriorityState`, `DeviceSelectionState` and `KeepAliveState` keep their in-memory lists and IDs: they are the live working state the pure, tested state machines run on, and making them read from settings would rework them and risk the behaviour tests. The context keeps saving to settings next to each state change, as today, but with one call per change instead of a playback/recording branch.
6. **Dead code removed:** the unused methods and the legacy `selectedDeviceId` handling, along with their tests.

## Non-goals

- No behaviour change the user can see: switching, restore, priority, keep-alive, balloons, the tray menu and the settings window all behave exactly as in v2.1.1. Anything else spotted along the way is written down, not fixed.
- No downgrade support. The user's decision: an older AudioLeash may fail to read the new file and start fresh.
- No upgrade from formats older than v2.1.x. The user's decision: converting from the current format is enough.
- No hand-editing support. The user's decision: the file is entirely app-managed, so the layout has no readability constraints beyond staying JSON.
- No new settings, and nothing from DEVELOPMENT.md's roadmap.

## Anti-goals (it fails if…)

- …after upgrading, the user has to set anything up again: ranking, keep-alive, the pick, or the settings window's state.
- …a missing, corrupt or unreadable file produces an error dialog or crash, instead of the current silent "start empty".
- …first-run detection changes. The welcome balloon still shows only when no settings file exists.
- …a failed write blocks or breaks anything. Saving stays best-effort and silent, as today.

## The new file

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
- `pick` is null when nothing is saved. In legacy mode it is the locked device; in priority mode it is the manual override (the same meaning the `selected*` fields have today).
- Keep-alive stays top-level, because it applies to playback only.
- Property names stay camelCase, as today. Still written to `%AppData%\AudioLeash\settings.json`.

## Upgrade from the current file

On load, a file with no `version` is read in the current v2.1.x shape and converted:

| Current field | New field |
|---|---|
| `selectedPlaybackDeviceId` (falling back to `selectedDeviceId`) + `selectedPlaybackDeviceName` | `playback.pick` (description null) |
| `selectedCaptureDeviceId` + `selectedCaptureDeviceName` | `recording.pick` (description null) |
| `playbackPriority` | `playback.priority` |
| `capturePriority` | `recording.priority` |
| `keepAliveDevices` | `keepAlive` |

- A pick with an ID but no name gets the name `"Unknown device"`, matching how ranked entries with a missing name are already sanitized. *(Designer's call:)* the one visible side effect is accepted. A nameless pick can only come from a damaged or very old file, or from saving a pick whose name could never be resolved, so in practice it doesn't occur. In that case the tooltip reads "Unknown device (waiting)" instead of "Unknown (waiting)". The rebind then looks for a device named "Unknown device", finds none, and does nothing.
- The converted settings are written back in the new format straight away, so the conversion runs once.
- An upgraded pick has no description, so `DeviceRebinder`'s rule that a null description matches on name alone stays. From now on, a newly saved pick records its description (`MMDevice.DeviceFriendlyName`), like ranked entries do.
- A file with a `version` higher than 2 is treated like a corrupt file: start empty (the user doesn't care about downgrades, and this is the mirror case).

## A file that exists but won't load

Today every save re-reads the file, so a read that fails once (antivirus or a backup tool briefly holding the file) costs nothing. Loading once into memory would turn that brief failure into permanent loss: the first save would overwrite the real file with empty settings. That breaks the anti-goal of never making the user set things up again. *(Designer's call:)*

- At load, a read that fails with an I/O error (locked, sharing violation) is retried a few times over about a second before giving up.
- If the file still can't be used (unreadable, invalid JSON, unknown version), the app starts empty as today. Before its first write that session, it copies the existing file to `settings.json.bak`, overwriting any older backup, so the last good file is never destroyed silently. Nothing reads the backup automatically; it is a safety net only.
- Tests cover: a corrupt file is backed up on the first save; a good file is never backed up; a missing file creates no backup.

Load-time sanitizing stays as it is today for every list (drop null and empty IDs, keep the first of duplicate IDs, replace a missing name). The same sanitizing applies to a pick.

## Code shape

- **`SettingsService`** owns the in-memory settings. It loads the file once, in its constructor or on first access, and answers reads from memory. Each change updates memory and then writes the whole file. This replaces today's read-modify-write of individual fields.
- **API:** per-flow access through a small record, e.g. `FlowSettings(RankedDevice? Pick, IReadOnlyList<RankedDevice> Priority)`, plus the keep-alive list:
  - read: `GetFlow(flow)`, `KeepAlive`, `HasSettingsFile`
  - write: `SetPick(flow, RankedDevice?)`, `SetPriority(flow, list)`, `SetFlow(flow, list, pick)` (one write for both, as `SavePlaybackFlow` does today), `SetKeepAlive(list)`
- **Flow parameter:** `SettingsService` stays free of NAudio. It uses its own two-value enum (playback, recording); `AudioLeashContext` maps `DataFlow` to it in one place.
- **`AudioLeashContext`:**
  - drops `_savedPlaybackDeviceName` and `_savedCaptureDeviceName` and reads the pick name from `SettingsService` instead;
  - replaces each `if (flow == DataFlow.Render) { …playback… } else { …capture… }` save block with a single call that takes the flow;
  - saves picks as a `RankedDevice` that includes the description;
  - `RebindSavedPick` and the startup rebinds use the new API, unchanged in behaviour.
- **Removed:** `LoadSelectedDeviceId`, `SaveSelectedDeviceId`, `SaveSelectedPlaybackDeviceId`, `SaveSelectedCaptureDeviceId`, `SaveSelectedDeviceIds`, and every per-flow twin replaced by the API above.

## Tests

- `SettingsServiceTests` is rewritten for the new API:
  - round trips per flow;
  - each write preserves the other flow and the keep-alive list;
  - missing file, corrupt file and an unknown future version all start empty;
  - `HasSettingsFile` behaves as today;
  - sanitizing.
- New upgrade tests, which feed in a real v2.1.x file (the user's current file shape is a good fixture):
  - every field lands in the right place;
  - a pick with no name gets the fallback name;
  - `selectedDeviceId` is used as the fallback for the playback pick;
  - the file is rewritten as version 2;
  - a second load reads version 2 directly.
- Existing behaviour tests elsewhere must pass unchanged.

## Left to the builder

- Exact type and method names, and whether `FlowSettings` is a record or a class.
- Whether the upgrade lives inside `SettingsService` or in a small separate pure class. The latter is preferred if it keeps the tests simpler.
- How the in-memory settings are guarded. Today all settings calls come from the UI thread; keep it that way, and document it rather than adding locking.
