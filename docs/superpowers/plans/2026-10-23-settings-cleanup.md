# Settings Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the live state the one canonical copy of AudioLeash's settings, with a versioned `settings.json` that is rebuilt in full from that state on every save.

**Architecture:**
- `PriorityState` and `DeviceSelectionState` hold the whole pick as a `RankedDevice`.
- A new `SettingsStore` loads and saves an immutable `SettingsSnapshot`. It handles the v1→v2 upgrade, retries a locked file, backs up an unusable one and skips identical writes.
- `AudioLeashContext` builds the snapshot from live state in one `SaveSettings()` method, called after every change.
- The old `SettingsService` and the context's name copies are deleted.

**Tech Stack:** C# 14 / .NET 10 (`net10.0-windows`), WinForms, NAudio.Wasapi 2.2.1, System.Text.Json, xUnit + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-10-23-settings-cleanup-design.md`

## Global Constraints

- No behaviour change the user can see, apart from the two edges the spec accepts:
  - a nameless pick shows "Unknown device";
  - an emptied list on exit still saves when the default lookup fails.
- File: `%AppData%\AudioLeash\settings.json`, camelCase, indented. Top level `"version": 2`, with `playback` and `recording` (each `{ pick, priority }`) and `keepAlive`.
- A file with no `version` is v2.1.x and must be upgraded. Older formats need no support, beyond `selectedDeviceId` as the fallback for the playback pick.
- A `version` higher than 2 is treated like a corrupt file.
- Sanitizing on every list and on the pick:
  - drop a null entry or one with a null/empty `Id`;
  - keep the first of duplicate IDs;
  - a null/empty name becomes `"Unknown device"`.
- Locked-file retry: up to 4 read attempts, 250 ms apart, on `IOException` only.
- Backup: `settings.json.bak`, overwritten. If the copy fails, nothing is written, and the copy is retried on the next save.
- First-run detection is unchanged: no file must be created until there is something non-empty to save.
- All settings loads and saves run on the UI thread. Saving never throws.
- `#nullable enable` in every new file. Comment density and naming follow the surrounding code.
- `dotnet build AudioLeash.sln -c Release` has no warnings, and `dotnet test AudioLeash.sln` passes at every commit.
- Commits use conventional prefixes and end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A fresh install with no picks.** Starting up and saving the empty settings must not create a file, so the welcome balloon shows again on the next launch. Test: `Save_EmptySnapshotWithNoFile_WritesNothing` (Task 2).
2. **The user's real v2.1.1 file.** It upgrades with the TV ranked first and the keep-alive entry intact. Test: `Parse_UserV21File_Upgrades` (Task 1), using the exact JSON fixture given there.
3. **An unranked manual override ending.** The "back to priority order" balloon still names it, rather than saying "device". Test: `EvaluateSettled_OverrideEnded_CarriesEndedOverride` (Task 3).
4. **The file is locked for the whole retry window.** The good file must not be replaced. Test: `Save_AfterUnusableLoad_BackupFails_WritesNothingAndRetriesNextSave` (Task 2).
5. **Logoff with the settings window open, after the user emptied a ranked list.** The emptied list is saved even if reading the Windows default fails. This is a manual check in Task 5, Step 6, because the path runs through COM. Its pure part is `FlowSnapshot.From` with an empty list (Task 1).

---

### Task 1: Snapshot types and the file format

**Files:**
- Create: `AudioLeash/SettingsSnapshot.cs`
- Create: `AudioLeash/SettingsFile.cs`
- Test: `AudioLeash.Tests/SettingsFileTests.cs`

**Interfaces:**
- Produces, in `SettingsSnapshot.cs`:
  - `public sealed record FlowSnapshot(RankedDevice? Pick, IReadOnlyList<RankedDevice> Priority)`, with:
    - `public static FlowSnapshot Empty { get; }` (null pick, empty list);
    - `public static FlowSnapshot From(IReadOnlyList<RankedDevice> ranked, RankedDevice? overrideDevice, RankedDevice? selectedDevice)`: the pick is `overrideDevice` when `ranked.Count > 0`, else `selectedDevice`.
  - `public sealed record SettingsSnapshot(FlowSnapshot Playback, FlowSnapshot Recording, IReadOnlyList<RankedDevice> KeepAlive)`, with `public static SettingsSnapshot Empty { get; }`.
- Produces, in `SettingsFile.cs` (`internal static class SettingsFile`):
  - `public const int CurrentVersion = 2;`
  - `public static SettingsSnapshot? Parse(string json)`:
    - returns null for invalid JSON, a non-object root or a version above 2;
    - converts a v1 file (no `version`);
    - sanitizes everything.
  - `public static string Serialize(SettingsSnapshot snapshot)`: writes the v2 shape.
- The DTO records for v1 and v2 are private nested types. The v1 field names are exactly today's `AppSettings` fields in `SettingsService.cs`.

- [ ] **Step 1: Write the failing tests** in `SettingsFileTests.cs`:
  - `FlowSnapshot_From_PriorityMode_TakesOverride`: ranked `[A]`, override `B`, selected `C` gives `B`.
  - `FlowSnapshot_From_LegacyMode_TakesSelected`: ranked `[]`, override `B`, selected `C` gives `C`.
  - `FlowSnapshot_From_NoPick_IsNull`: ranked `[]` with both null gives a null pick and an empty list.
  - `Serialize_ThenParse_RoundTrips`: playback pick and list, a recording list, keep-alive, all with descriptions, compared field by field.
  - `Serialize_WritesVersion2AndCamelCase`: the JSON contains `"version": 2`, `"playback"`, `"recording"`, `"keepAlive"`, `"pick"` and `"priority"`.
  - `Parse_UserV21File_Upgrades`, with this fixture verbatim:
    ```json
    {"selectedDeviceId":null,"selectedPlaybackDeviceId":null,"selectedCaptureDeviceId":null,"selectedPlaybackDeviceName":null,"selectedCaptureDeviceName":null,
     "playbackPriority":[{"id":"{0.0.0.00000000}.{c0df5970-fe89-4405-b2d1-3e76cd2e0868}","name":"LG TV SSCR2 (NVIDIA High Definition Audio)","description":"NVIDIA High Definition Audio"},{"id":"{0.0.0.00000000}.{5472e742-afe0-4efe-8445-eff9aba2bea5}","name":"Headphones (Arctis Nova Elite)","description":"Arctis Nova Elite"}],
     "capturePriority":[{"id":"{0.0.1.00000000}.{c51338b5-079c-41b7-9f62-c603f0855872}","name":"Microphone (Arctis Nova Elite)","description":"Arctis Nova Elite"}],
     "keepAliveDevices":[{"id":"{0.0.0.00000000}.{c0df5970-fe89-4405-b2d1-3e76cd2e0868}","name":"LG TV SSCR2 (NVIDIA High Definition Audio)","description":"NVIDIA High Definition Audio"}]}
    ```
    Assert:
    - both picks are null;
    - the playback priority holds the two entries in order, with names and descriptions;
    - the recording priority holds the microphone;
    - keep-alive holds the TV.
  - `Parse_V1Picks_MapWithNullDescription`: `selectedPlaybackDeviceId` + name and `selectedCaptureDeviceId` + name become picks with `Description == null`.
  - `Parse_V1_LegacySelectedDeviceId_IsPlaybackFallback`: only `selectedDeviceId` + `selectedPlaybackDeviceName` set gives that playback pick.
  - `Parse_V1_PickWithoutName_GetsUnknownDevice`: the pick name is `"Unknown device"`.
  - `Parse_SanitizesListsAndPick` (v2 input):
    - entries with an empty or null id are dropped;
    - of duplicate ids, the first wins;
    - a null name becomes `"Unknown device"`;
    - a pick with an empty id becomes a null pick.
  - `Parse_FutureVersion_ReturnsNull`: `{"version":3}`.
  - `Parse_InvalidJson_ReturnsNull`: `"not json"`.
  - `Parse_EmptyObject_IsEmptySnapshot`: `{}` (a v1 file with nothing set) gives null picks and empty lists.
- [ ] **Step 2: Run the tests and confirm they fail to compile**
  Run: `dotnet test AudioLeash.sln --filter FullyQualifiedName~SettingsFileTests`
  Expected: build errors naming `FlowSnapshot`, `SettingsSnapshot` and `SettingsFile`.
- [ ] **Step 3: Implement the two files.**
  - `Parse` first reads the root as a `JsonDocument` to look for `version`, then deserializes it as the v1 or v2 DTO.
  - Any `JsonException` gives null.
  - Use the same `JsonSerializerOptions` as today (`WriteIndented = true`, camelCase).
  - Move today's `Sanitize` from `SettingsService` here. Do not delete it from `SettingsService` yet.
- [ ] **Step 4: Run the tests and confirm they pass**
  Run: `dotnet test AudioLeash.sln`
  Expected: `Passed!`, with no failures.
- [ ] **Step 5: Commit**
  `git add AudioLeash/SettingsSnapshot.cs AudioLeash/SettingsFile.cs AudioLeash.Tests/SettingsFileTests.cs && git commit -m "feat: add versioned settings snapshot and file format"`

---

### Task 2: SettingsStore (I/O, retry, backup, no-op skip)

**Files:**
- Create: `AudioLeash/SettingsStore.cs`
- Test: `AudioLeash.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `SettingsFile.Parse`, `SettingsFile.Serialize`, `SettingsSnapshot.Empty` (Task 1).
- Produces:
  - `internal interface ISettingsFileSystem`, with members:
    - `bool Exists(string path)`
    - `string ReadAllText(string path)`
    - `void WriteAllText(string path, string text)`
    - `void Copy(string source, string dest, bool overwrite)`
    - `void CreateDirectory(string path)`
    - `void Delay(TimeSpan delay)`
  - `public sealed class SettingsStore`, with:
    - `public SettingsStore()`, which uses `%AppData%\AudioLeash` and the real file system;
    - `internal SettingsStore(string directory, ISettingsFileSystem? fileSystem = null)`;
    - `public bool HasSettingsFile { get; }`: whether the file existed at `Load()`;
    - `public SettingsSnapshot Load()`;
    - `public void Save(SettingsSnapshot snapshot)`.
- Behaviour that later tasks rely on:
  - `Load()` never throws. A missing file gives `Empty`. An unusable file gives `Empty` and marks the store as "backup before the first write".
  - `Save()` never throws, and skips the write when the serialized JSON equals the last JSON loaded or written.
  - For a missing file, the "last JSON" is the serialized `SettingsSnapshot.Empty`.
  - For an unusable file, the "last JSON" is null, so the first save writes, after the backup.

- [ ] **Step 1: Write the failing tests** in `SettingsStoreTests.cs`.
  - Use a temp directory with the real file system, except where a fault must be injected. There, use `Substitute.ForPartsOf` on a small test implementation of `ISettingsFileSystem`, or a hand-written fake.
  - Tests:
    - `Load_NoFile_ReturnsEmptyAndHasSettingsFileFalse`
    - `Save_EmptySnapshotWithNoFile_WritesNothing`: no file exists after `Save(SettingsSnapshot.Empty)`.
    - `Save_ThenNewStoreLoad_RoundTrips`, with `HasSettingsFile` true on the second store.
    - `Save_IdenticalSnapshot_DoesNotWriteAgain`: the fake counts `WriteAllText` calls, so two equal saves make 1 write.
    - `Load_V1File_UpgradedOnFirstSave`: write a v1 JSON, `Load()`, then `Save(loaded)`. The file now contains `"version": 2`.
    - `Load_LockedFile_RetriesThenSucceeds`: the fake throws `IOException` on the first 2 reads, then returns valid JSON. `Load` returns that content, after 2 `Delay(250 ms)` calls.
    - `Load_LockedFileForAllAttempts_ReturnsEmpty`: 4 reads, 3 delays.
    - `Save_AfterCorruptLoad_BacksUpThenWrites`: corrupt file, `Load`, `Save(non-empty)`. `.bak` holds the corrupt text, and the main file holds the new JSON.
    - `Save_AfterUnusableLoad_BackupFails_WritesNothingAndRetriesNextSave`:
      - the first `Copy` throws, so no write happens;
      - the second save's `Copy` succeeds, and the write goes ahead.
    - `Save_GoodFile_NeverBacksUp`: no `.bak` after load and save.
    - `Save_NoFile_NeverBacksUp`.
    - `Save_WriteFails_DoesNotThrow`: the fake's `WriteAllText` throws.
- [ ] **Step 2: Run the tests and confirm they fail**
  Run: `dotnet test AudioLeash.sln --filter FullyQualifiedName~SettingsStoreTests`
  Expected: build errors naming `SettingsStore`.
- [ ] **Step 3: Implement `SettingsStore.cs`**, with a private `RealFileSystem : ISettingsFileSystem` (`Delay` is `Thread.Sleep`). Only `IOException` is retried. `UnauthorizedAccessException` and parse failures go straight to "unusable".
- [ ] **Step 4: Run the tests and confirm they pass**
  Run: `dotnet test AudioLeash.sln`
  Expected: `Passed!`
- [ ] **Step 5: Commit**
  `git commit -m "feat: add settings store with upgrade, locked-file retry and backup"`

---

### Task 3: State machines hold the whole pick

**Files:**
- Modify: `AudioLeash/DeviceSelectionState.cs`
- Modify: `AudioLeash/PriorityState.cs`
- Modify: `AudioLeash/AudioLeashContext.cs`, only call sites, to keep it compiling
- Test: `AudioLeash.Tests/DeviceSelectionStateTests.cs`, `AudioLeash.Tests/PriorityStateTests.cs`

**Interfaces:**
- Produces, in `DeviceSelectionState`:
  - `public RankedDevice? SelectedDevice { get; }`, backed by a `volatile` field;
  - `public string? SelectedDeviceId => SelectedDevice?.Id;`;
  - `public void SelectDevice(RankedDevice device)`, which replaces `SelectDevice(string)`.
- Produces, in `PriorityState`:
  - `public RankedDevice? Override { get; }`, under the lock;
  - `public string? OverrideId => Override?.Id;`;
  - `public void StartOverride(RankedDevice device)`, which replaces `StartOverride(string)`.
- `PriorityDecision` becomes `PriorityDecision(PriorityOutcome Outcome, string? TargetId, string? PreviousTargetId, RankedDevice? EndedOverride = null)`. `EndedOverride` is set only for `OverrideEnded`.
- `PlanPersistedPick` stays for now and is removed in Task 5.

- [ ] **Step 1: Update the tests.**
  - Add a helper `D(string id) => new(id, id + " name")` where the tests don't already have one.
  - Change every `SelectDevice("x")` and `StartOverride("x")` to pass `D("x")`. The assertions don't change.
  - Add:
    - `SelectDevice_HoldsWholeDevice`: `SelectedDevice` equals the device passed in, and `SelectedDeviceId` is its id.
    - `StartOverride_HoldsWholeDevice`: the same for `Override` and `OverrideId`.
    - `EvaluateSettled_OverrideEnded_CarriesEndedOverride`: override `D("usb")`, not connected, outside grace. The decision's `EndedOverride` equals `D("usb")`, and `Override` is null afterwards.
    - `EvaluateSettled_OtherOutcomes_HaveNoEndedOverride`: the `SwitchedUp` case gives null.
- [ ] **Step 2: Run the tests and confirm they fail to compile**
  Run: `dotnet test AudioLeash.sln`
  Expected: errors about converting `RankedDevice` to `string` in the test files.
- [ ] **Step 3: Implement the state changes, and fix the context call sites with no behaviour change.** Each site builds a `RankedDevice`:
  - `DeviceMenuItem_Click` and `PickPriorityDevice`: from the `MMDevice` (`ID`, `FriendlyName`, `DeviceFriendlyName`). Change `PickPriorityDevice`'s parameters to `(DataFlow flow, PriorityState priority, RankedDevice device)`. Its rollback restores the previous `Override` object.
  - Startup (`StartFlow`, `TryRestoreSavedDevice`), the rebinds and `ApplyFlowSettings`: from the saved id and the saved name they already have (`new RankedDevice(id, name ?? "Unknown device")`). Task 5 replaces these.
- [ ] **Step 4: Run the tests and confirm they pass**
  Run: `dotnet build AudioLeash.sln -c Release` then `dotnet test AudioLeash.sln`
  Expected: 0 warnings and `Passed!`
- [ ] **Step 5: Commit**
  `git commit -m "refactor: hold the whole pick in the selection and priority state"`

---

### Task 4: Pure startup refresh of a snapshot

**Files:**
- Modify: `AudioLeash/DeviceRebinder.cs`
- Test: `AudioLeash.Tests/DeviceRebinderTests.cs`

**Interfaces:**
- Consumes: `SettingsSnapshot` and `FlowSnapshot` (Task 1), and the existing `DeviceRebinder.Rebind`.
- Produces:
  - `public sealed record KnownDevices(IReadOnlySet<string> ExistingIds, IReadOnlyList<DeviceRow> Candidates)`, in `DeviceRebinder.cs`. It replaces the context's private `RebindDevices` record in Task 5.
  - `public static RankedDevice? RefreshPick(RankedDevice? pick, IReadOnlyList<DeviceRow> candidates)`:
    - a pick whose `Id` matches a candidate takes that candidate's `Name` and `Description`;
    - otherwise the pick is returned unchanged (the same instance);
    - null gives null.
  - `public static SettingsSnapshot Refresh(SettingsSnapshot snapshot, KnownDevices? playback, KnownDevices? recording, out bool changed)`, applied per flow:
    - rebind the priority list;
    - rebind the pick (as a one-element list);
    - then `RefreshPick` the result.

    Keep-alive is rebound against `playback`. A null `KnownDevices` leaves that flow, and for `playback` the keep-alive list too, untouched. `changed` is true when any entry's id, name or description changed.

- [ ] **Step 1: Write the failing tests:**
  - `RefreshPick_MatchingId_TakesCurrentNameAndDescription`: pick `("tv", "old name", null)` with candidate `Row("tv", …)` gives the candidate's name and description.
  - `RefreshPick_NoMatch_ReturnsSameInstance`
  - `RefreshPick_Null_ReturnsNull`
  - `Refresh_RebindsListsPickAndKeepAlive`: a snapshot whose TV id `old` is gone from `ExistingIds`, with candidate `new`. The ranked entry, the playback pick and the keep-alive entry all become `new`, and `changed` is true.
  - `Refresh_UpgradedPickGainsDescription`: the pick has a null description and its id exists. Afterwards the description is the candidate's, and `changed` is true.
  - `Refresh_NothingToDo_ChangedFalse`
  - `Refresh_NullKnownDevices_LeavesFlowUntouched`
- [ ] **Step 2: Run the tests and confirm they fail**
  Run: `dotnet test AudioLeash.sln --filter FullyQualifiedName~DeviceRebinderTests`
  Expected: build errors naming `KnownDevices`, `RefreshPick` and `Refresh`.
- [ ] **Step 3: Implement**, by composing `Rebind` and `RefreshPick`. No new matching rules.
- [ ] **Step 4: Run the tests and confirm they pass**
  Run: `dotnet test AudioLeash.sln`
  Expected: `Passed!`
- [ ] **Step 5: Commit**
  `git commit -m "feat: refresh a settings snapshot against the known devices"`

---

### Task 5: Switch the context to one canonical copy, and remove the old service

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs`
- Modify: `AudioLeash/PriorityState.cs` (remove `PlanPersistedPick`)
- Delete: `AudioLeash/SettingsService.cs`, `AudioLeash.Tests/SettingsServiceTests.cs`
- Modify: `AudioLeash.Tests/PriorityStateTests.cs` (remove `PlanPersistedPick_CoversAllFourTransitions`; `FlowSnapshot.From` tests now cover the rule)
- Modify: `DEVELOPMENT.md`, `CLAUDE.md`, `RELEASE_NOTES.md`, `AudioLeash/AudioLeash.csproj` (version)

**Interfaces:**
- Consumes everything from Tasks 1–4.
- Produces these private members in the context:
  - `private SettingsSnapshot BuildSnapshot()`: per flow `FlowSnapshot.From(priority.Ranked, priority.Override, selection.SelectedDevice)`, plus `_keepAlive.State.Devices`;
  - `private void SaveSettings() => _settings.Save(BuildSnapshot());`;
  - `private KnownDevices? TryGetKnownDevices(DataFlow flow)`: the existing `TryGetRebindDevices`, renamed and returning `KnownDevices`;
  - `private RankedDevice? TryGetDeviceDetails(string id)`: `_enumerator.GetDevice`, giving `ID`, `FriendlyName` and `DeviceFriendlyName`, or null on any exception.

- [ ] **Step 1: Startup.**
  - Replace the field `_settingsService` with `private readonly SettingsStore _settings`.
  - Replace the constructor's load sequence with:
    - `var loaded = _settings.Load()`;
    - `var snapshot = DeviceRebinder.Refresh(loaded, TryGetKnownDevices(Render), TryGetKnownDevices(Capture), out _)`;
    - `keepAlive.State.ReplaceAll(snapshot.KeepAlive)`;
    - `StartFlow(flow, flowSnapshot.Priority, flowSnapshot.Pick, …)`;
    - after both flows, call `SaveSettings()` unconditionally. The no-op skip makes this cheap, and it also writes the upgrade.
  - `StartFlow` and `TryRestoreSavedDevice` take `RankedDevice? pick` instead of an id.
  - `isFirstRun` becomes `snapshot.Playback.Pick is null && !_settings.HasSettingsFile`.
- [ ] **Step 2: Every change path ends in `SaveSettings()`, and the per-flow save branches are deleted.** These are:
  - `DeviceMenuItem_Click` (legacy pick);
  - `PickPriorityDevice` (after a successful switch only; a failed switch saves nothing, as today);
  - `ReturnToPriority_Click`;
  - `ClearSelection_Click`;
  - `ApplyPriorityDecision` (an override ending);
  - `OnKeepAliveToggled`;
  - `RebindReplacedDevices`;
  - `ApplyFlowSettings`.
- [ ] **Step 3: Names come from the live state.**
  - Delete `_savedPlaybackDeviceName` and `_savedCaptureDeviceName`.
  - Replacements:
    - the legacy unavailable menu entry and tooltip use `selection.SelectedDevice?.Name`;
    - the priority unavailable entry and the grace tooltip use `priority.Override?.Name`;
    - the ended-override balloon name uses `decision.EndedOverride?.Name` as the last fallback in `ResolvePriorityName`.
  - Keep the existing fallback strings exactly: `"Unknown Device"` in the menu, `"Unknown"` in the tooltip, `"device"` in balloons.
- [ ] **Step 4: Runtime rebind on the live state.**
  - Rewrite `RebindReplacedDevices` to rebind `priority.Ranked`, the live pick (`priority.Override` in priority mode, else `selection.SelectedDevice`) and `_keepAlive.State.Devices` with `DeviceRebinder.Rebind`.
  - Apply the results through `SetRanked`, `StartOverride` / `SelectDevice` (keeping `IsDeviceAvailable`, as today) and `ReplaceAll`.
  - Then call `SaveSettings()` once, and reconcile the changed flows.
  - Delete `RebindSaved`, `RebindSavedPick` and the `RebindDevices` record.
- [ ] **Step 5: `ApplyFlowSettings` without `PlanPersistedPick`.**
  - Apply the new list to the live state first, then `SaveSettings()` once.
  - Mode changes:
    - **entering or staying in priority mode:** `selection.ClearSelection()` and `priority.SetRanked(newList)`;
    - **leaving priority mode:** `priority.SetRanked(newList)` and `priority.ReturnToPriority(active)`, then `selection.SelectDevice(...)` with the result of `TryGetDeviceDetails(currentDefaultId)`. If the default can't be read, skip `SelectDevice`.
  - When `exiting`:
    - use an empty active set, and make no other COM call;
    - skip `EvaluateSettled`, `ApplyPriorityDecision` and the balloons;
    - still call `SaveSettings()`.

    The one COM call kept is `GetCurrentDefaultId` and the details lookup when leaving priority mode, both best-effort. A failure must not skip the save: catch it around the lookup only.
  - Remove `PriorityState.PlanPersistedPick` and its test.
- [ ] **Step 6: Delete the old service, build and test, then do a manual check.**
  - Delete `SettingsService.cs` and `SettingsServiceTests.cs`.
  - Run: `dotnet build AudioLeash.sln -c Release` then `dotnet test AudioLeash.sln`
  - Expected: 0 warnings and `Passed!`
  - Manual checks, on Windows, with a backup copy of the user's `%AppData%\AudioLeash\settings.json` made first:
    - launch the Release build; `settings.json` is now version 2 with the TV first and keep-alive intact;
    - pick a device from the tray, quit, relaunch: the pick is restored;
    - open the settings window, empty the playback list, sign out with the window still open, sign back in: the list is empty and saved.
- [ ] **Step 7: Docs.**
  - **DEVELOPMENT.md:**
    - replace the structure lines for `SettingsService.cs` and `SettingsServiceTests.cs` with `SettingsSnapshot.cs`, `SettingsFile.cs`, `SettingsStore.cs`, `SettingsFileTests.cs` and `SettingsStoreTests.cs`;
    - rewrite the "override reuses the `Selected*` fields" and "persisted … via `SettingsService`" notes into one note: live state is canonical, the file is a v2 snapshot built by `BuildSnapshot`, and it covers the upgrade, retry and backup.
  - **CLAUDE.md:** the same structure lines, and the persistence bullet under Key Design Constraints.
  - **RELEASE_NOTES.md:** a `v2.1.2` section with one line: settings are stored in a new versioned format and upgraded automatically.
  - **`AudioLeash.csproj`:** `<Version>2.1.2</Version>`.
- [ ] **Step 8: Commit**
  `git commit -m "refactor: make live state the single canonical copy of settings"`
