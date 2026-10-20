# Device Priority Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user rank playback and recording devices so AudioLeash keeps the default on the highest-ranked connected device. A manual tray pick is a persisted override that ends only on "Return to priority order" or when that device disconnects. Rankings are managed in a new settings window.

**Architecture:** A new pure `PriorityState` per flow (no NAudio/WinForms) makes every priority decision: target selection, override lifecycle, startup grace, the default-change verdict and the save-time mode transition. `AudioLeashContext` keeps today's `DeviceSelectionState` path for flows with an empty list (legacy mode), and routes flows with a list through `PriorityState`. Both paths reuse the existing per-flow debounce timers. A pure `PriorityListEditor` backs a WinForms `SettingsForm` (tabs: Playback / Recording / General). `SettingsService` persists the ranked lists. The persisted override reuses the existing `Selected*Device*` fields.

**Tech Stack:** C# 14 / .NET 10 WinForms, NAudio.Wasapi 2.2.1, xUnit + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-10-17-device-priority-design.md`

## Global Constraints

- App targets `net10.0-windows`, tests target `net10.0-windows7.0`. `#nullable enable` in every new file (matches existing files).
- Dispose every `MMDevice` you enumerate (pattern: `foreach (var d in list) d.Dispose();`, as in `IsDeviceActive`).
- Audio-thread callbacks reach UI objects only via `SafeInvoke`. `PolicyConfigClient.SetDefaultEndpoint` runs on the UI thread only.
- React to `Role.Multimedia` only. Don't change `PolicyConfigClient`.
- A flow with an empty ranked list behaves **exactly as today**. Its existing tests in `DeviceSelectionStateTests` must keep passing unchanged.
- Unranked devices are never chosen automatically.
- Any device connecting, of any rank, never ends an override.
- Settings window: tabs **Playback**, **Recording**, **General**. The divider text is verbatim: `Not ranked: AudioLeash won't switch to these on its own`.
- Tray: new `Settings…` item. `Return to priority order` replaces `Clear Selection` in priority mode. Ranked devices show `  #N` after their name, and the override's line shows `  (manual)`.
- Branch `feature/device-priority`. Conventional commits ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Run `dotnet test AudioLeash.sln` (all green) before every commit that touches C#. Push after each commit (CLAUDE.md: never leave unpushed work).

## Review Focus

1. **Windows auto-switches to a just-plugged-in TV before the debounce settles.** AudioLeash must not yank the default back to the headphones and then, a second later, switch to the TV. Expect one switch, to the TV. *Pinned by Task 2 `EvaluateDefaultChange_NewDefaultOutranksTarget_NoOverride_ReturnsSuspend`.*
2. **Two ranked devices come up in the same debounce window** (e.g. TV and headphones dock after resume). Expect one settle, one switch to the higher device, one notification. *Pinned by Task 2 `EvaluateSettled_TwoRankedConnectTogether_SwitchesOnceToHighest`.*
3. **A ranked device that is gone from the system for good** (NotPresent, never enumerated). It stays in its ranked slot as "not connected", using its persisted name, without crashing. *Pinned by Task 3 `Constructor_RankedDeviceNotKnown_KeepsSlotWithPersistedNameAndDisconnected`.*
4. **Hand-edited or damaged `settings.json`** (null or empty IDs, duplicates, a null list). Loading yields a clean list and never throws. *Pinned by Task 1 `LoadPlaybackPriority_DropsEmptyIdsAndDuplicates`.*
5. **"Return to priority order" while the override is already the top device.** No redundant `SetDefaultEndpoint`, and no notification claiming a switch. *Pinned by Task 2 `ReturnToPriority_OverrideIsTopDevice_ReturnsSameTarget`, and by Task 5 calling `SetDefaultEndpoint` only when `GetCurrentDefaultId(flow) != target`.*

---

### Task 1: Persist ranked lists in `SettingsService`

**Files:**
- Create: `AudioLeash/RankedDevice.cs`
- Modify: `AudioLeash/SettingsService.cs` (every `Save*` method, `AppSettings` record)
- Test: `AudioLeash.Tests/SettingsServiceTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record RankedDevice(string Id, string Name, string? Description = null);` (namespace `AudioLeash`)
  - `SettingsService.LoadPlaybackPriority() -> IReadOnlyList<RankedDevice>` and `LoadCapturePriority() -> IReadOnlyList<RankedDevice>`. Both return an empty list when missing, never null.
  - `SettingsService.SavePlaybackPriority(IReadOnlyList<RankedDevice> list)` and `SaveCapturePriority(IReadOnlyList<RankedDevice> list)`. Both preserve all other fields.
  - JSON property names: `playbackPriority`, `capturePriority`, from the existing camelCase policy.

- [ ] **Step 1: Write the failing tests** in `SettingsServiceTests.cs`:

```csharp
[Fact]
public void LoadPlaybackPriority_WhenFileAbsent_ReturnsEmpty()
    => Assert.Empty(Svc().LoadPlaybackPriority());

[Fact]
public void SavePlaybackPriority_RoundTripsOrderAndFields()
{
    var svc = Svc();
    var list = new[] { new RankedDevice("tv", "LG TV", "NVIDIA HDA"), new RankedDevice("hp", "Headphones") };
    svc.SavePlaybackPriority(list);
    Assert.Equal(list, svc.LoadPlaybackPriority());
    Assert.Empty(svc.LoadCapturePriority());
}

[Fact]
public void SaveSelectedPlaybackDevice_PreservesPriorityLists()
{
    var svc = Svc();
    svc.SavePlaybackPriority(new[] { new RankedDevice("tv", "LG TV") });
    svc.SaveCapturePriority(new[] { new RankedDevice("mic", "Headset Mic") });
    svc.SaveSelectedPlaybackDevice("spk", "Speakers");
    svc.SaveSelectedCaptureDevice("cam", "Webcam");
    svc.SaveSelectedDeviceIds(null, null);
    Assert.Single(svc.LoadPlaybackPriority());
    Assert.Single(svc.LoadCapturePriority());
}

[Fact]
public void SavePlaybackPriority_PreservesSelections()
{
    var svc = Svc();
    svc.SaveSelectedPlaybackDevice("spk", "Speakers");
    svc.SavePlaybackPriority(new[] { new RankedDevice("tv", "LG TV") });
    Assert.Equal("spk", svc.LoadSelectedPlaybackDeviceId());
    Assert.Equal("Speakers", svc.LoadSelectedPlaybackDeviceName());
}

[Fact]
public void LoadPlaybackPriority_LegacyFileWithoutField_ReturnsEmptyAndKeepsSelection()
{
    Directory.CreateDirectory(_tempDir);
    File.WriteAllText(Path.Combine(_tempDir, "settings.json"),
        """{ "selectedPlaybackDeviceId": "spk" }""");
    var svc = Svc();
    Assert.Empty(svc.LoadPlaybackPriority());
    Assert.Equal("spk", svc.LoadSelectedPlaybackDeviceId());
}

[Fact]
public void LoadPlaybackPriority_DropsEmptyIdsAndDuplicates()
{
    Directory.CreateDirectory(_tempDir);
    File.WriteAllText(Path.Combine(_tempDir, "settings.json"), """
        { "playbackPriority": [
            { "id": "tv", "name": "LG TV" }, { "id": "", "name": "x" }, { "id": null, "name": "y" },
            { "id": "tv", "name": "dup" },   { "id": "hp", "name": null } ] }
        """);
    var list = Svc().LoadPlaybackPriority();
    Assert.Equal(new[] { "tv", "hp" }, list.Select(d => d.Id));
    Assert.Equal("LG TV", list[0].Name);
    Assert.Equal("Unknown device", list[1].Name);
}
```

- [ ] **Step 2: Run them to verify they fail.**
Run: `dotnet test AudioLeash.sln --filter "FullyQualifiedName~SettingsServiceTests"`
Expected: build errors (`RankedDevice`, `LoadPlaybackPriority` undefined).

- [ ] **Step 3: Implement.** Create `RankedDevice.cs`. Add `IReadOnlyList<RankedDevice>? PlaybackPriority = null, IReadOnlyList<RankedDevice>? CapturePriority = null` to `AppSettings`. Rewrite **every** existing `Save*` method as `SaveSettings((LoadSettings() ?? Empty) with { ... })`, setting only the fields it owns. That way, fields a method doesn't mention survive. Today they are dropped, because each method builds a fresh positional record. `SaveSelectedDeviceIds` still overwrites both selection fields and their names, but keeps the priority lists. Keep every existing behaviour its tests assert, including the legacy-field migration. The sanitizing in `Load*Priority` happens on read: skip null/empty `Id`, keep the first occurrence of each `Id`, and replace a null `Name` with `"Unknown device"`. JSON deserialization yields `null` strings despite the non-nullable record, so check with `string.IsNullOrEmpty`.

- [ ] **Step 4: Run the full suite.**
Run: `dotnet test AudioLeash.sln`
Expected: all pass, including the existing `SettingsServiceTests`.

- [ ] **Step 5: Commit and push.**
```bash
git add AudioLeash/RankedDevice.cs AudioLeash/SettingsService.cs AudioLeash.Tests/SettingsServiceTests.cs
git commit -m "feat: persist ranked device priority lists"
git push
```

---

### Task 2: Pure priority decision logic (`PriorityState`) and the legacy race fix

**Files:**
- Create: `AudioLeash/PriorityState.cs`
- Modify: `AudioLeash/DeviceSelectionState.cs` (add `DefaultChangeDecision` and the snapshot overload)
- Test: `AudioLeash.Tests/PriorityStateTests.cs`, `AudioLeash.Tests/DeviceSelectionStateTests.cs`

**Interfaces:**
- Consumes: `RankedDevice` (Task 1).
- Produces, in `DeviceSelectionState.cs`:
  - `internal readonly record struct DefaultChangeDecision(RestoreDecision Decision, string? RestoreId);`. When `Decision == Restore`, `RestoreId` is non-null.
  - `DeviceSelectionState.EvaluateDefaultChange(string newDefaultId, Func<string, bool> isDeviceActive) -> DefaultChangeDecision`. It reads `SelectedDeviceId` **once** into a local and uses that for both the availability check and `RestoreId`. The existing `EvaluateDefaultChange(string, bool)` becomes a one-liner delegating to it (`_ => isSelectedDeviceAvailable`), so the old tests still pass.
- Produces, in `PriorityState.cs`:
  - `internal enum PriorityOutcome { None, ReassertSilently, SwitchedUp, FellBack, OverrideEnded, NoneConnected }`
  - `internal readonly record struct PriorityDecision(PriorityOutcome Outcome, string? TargetId, string? PreviousTargetId);`
  - `internal sealed class PriorityState` with:
    - `IReadOnlyList<RankedDevice> Ranked { get; }`, `bool IsPriorityMode` (`Ranked.Count > 0`), `string? OverrideId { get; }`, `string? TargetId { get; }` (the last committed enforced device), `bool IsInternalChange { get; set; }` (volatile, same semantics as `DeviceSelectionState`), `bool InStartupGrace { get; }`
    - `void SetRanked(IReadOnlyList<RankedDevice> ranked)`: replaces the list. It does not touch the override or the target.
    - `int? RankOf(string id)`: 1-based, or null when unranked.
    - `bool IsRelevant(string id)`: true when the device is ranked or is the override.
    - `void Initialize(IReadOnlySet<string> connected)`: commits the startup target without producing a decision.
    - `void StartOverride(string id)`: sets the override and target to `id`.
    - `string? ReturnToPriority(IReadOnlySet<string> connected)`: clears the override, commits and returns the top connected ranked device (or null).
    - `void BeginStartupGrace()` / `void EndStartupGrace()`
    - `PriorityDecision EvaluateSettled(IReadOnlySet<string> connected)`
    - `DefaultChangeDecision EvaluateDefaultChange(string newDefaultId, Func<string, bool> isDeviceActive)`
    - `static string? PlanPersistedPick(bool wasPriority, bool isPriority, string? legacyPickId, string? overrideId, string? currentDefaultId)`
  - All mutable state except `IsInternalChange` is guarded by one private lock. `IsRelevant` and `EvaluateDefaultChange` are called from the audio thread.

**Rules the implementation must follow** (the spec's Behaviour section, made precise):

- *Top* = the first entry of `Ranked` whose `Id` is in `connected`, or null.
- `Initialize`: if there's an override, target = override when connected, else null. If there's no override, target = top.
- `EvaluateSettled`, with an override set:
  - Override connected: commit target = override, return `ReassertSilently`.
  - Override absent and `InStartupGrace`: commit target = null, return `None`.
  - Override absent otherwise: clear the override, commit target = top, return `OverrideEnded(top, previousOverride)`.
- `EvaluateSettled`, with no override: let `prev` = `TargetId`, then commit `TargetId = top`. Then:
  - `top == prev`: `ReassertSilently` if non-null, else `None`.
  - `top == null`: `NoneConnected`.
  - `prev == null` or `connected.Contains(prev)`: `SwitchedUp`.
  - Otherwise: `FellBack`.
- `EvaluateDefaultChange`, in this order (snapshot `TargetId` and `OverrideId` once, under the lock):
  1. `IsInternalChange`: `NoAction`.
  2. Target null: `NoAction`.
  3. `newDefaultId == target`: `NoAction`.
  4. `!isDeviceActive(target)`: `Suspend`.
  5. No override and `RankOf(newDefaultId) < RankOf(target)`: `Suspend` (defer to the settled reconcile; Review Focus 1).
  6. Otherwise: `Restore(target)`.

  Document in the XML comments that `Suspend` means "defer to the debounced reconcile".
- `PlanPersistedPick`, per flow:
  - Legacy to legacy: `legacyPickId`.
  - Legacy to priority: `null`.
  - Priority to priority: `overrideId`.
  - Priority to legacy: `currentDefaultId`.

- [ ] **Step 1: Write the failing tests** in `PriorityStateTests.cs`. Use the helper `static PriorityState Make(params string[] ids)`, which ranks `ids` in order with `Name = id`, and `static HashSet<string> On(params string[] ids)`. Write one `[Fact]` per line below (each line gives the name, then the setup and the expected result):
  - `Initialize_NoOverride_TargetsTopConnected`: ranked tv,hp,spk, connected hp,spk. `TargetId == "hp"`.
  - `Initialize_NoRankedConnected_TargetIsNull`
  - `EvaluateSettled_HigherRankedConnects_SwitchesUp`: init with hp. Then connected tv,hp gives `(SwitchedUp, "tv", "hp")`.
  - `EvaluateSettled_TargetDisconnects_FallsBack`: target tv. Then connected hp gives `(FellBack, "hp", "tv")`.
  - `EvaluateSettled_LastRankedDisconnects_ReturnsNoneConnected`
  - `EvaluateSettled_UnrankedConnects_ReturnsReassertSilently` (target unchanged)
  - `EvaluateSettled_FlapEndsWhereItStarted_ReturnsReassertSilently`
  - `EvaluateSettled_TwoRankedConnectTogether_SwitchesOnceToHighest`: target spk. Then connected tv,hp,spk gives `(SwitchedUp, "tv", "spk")`. A second call with the same set gives `ReassertSilently`.
  - `EvaluateSettled_OverrideHeld_WhenHigherRankedConnects`: override spk. Then connected tv,hp,spk gives `ReassertSilently`, and `OverrideId == "spk"`.
  - `EvaluateSettled_OverrideOnUnrankedDevice_HeldWhenRankedConnects`
  - `EvaluateSettled_OverrideDisconnects_EndsOverrideAndFallsBackToTop`: override spk, connected hp. Expect `(OverrideEnded, "hp", "spk")` and `OverrideId == null`.
  - `EvaluateSettled_OverrideAbsentDuringStartupGrace_KeepsOverride`: returns `None`, the override is kept. After `EndStartupGrace`, the next call returns `OverrideEnded`.
  - `ReturnToPriority_ClearsOverrideAndReturnsTop`
  - `ReturnToPriority_OverrideIsTopDevice_ReturnsSameTarget`: override tv, connected tv,hp. Returns `"tv"`, and `TargetId == "tv"`.
  - `SetRanked_KeepsOverride`
  - `EvaluateDefaultChange_IsInternalChange_ReturnsNoAction`
  - `EvaluateDefaultChange_NoTarget_ReturnsNoAction`
  - `EvaluateDefaultChange_ExternalChangeToLowerDevice_RestoresTarget`: `(Restore, "tv")`
  - `EvaluateDefaultChange_TargetInactive_ReturnsSuspend`
  - `EvaluateDefaultChange_NewDefaultOutranksTarget_NoOverride_ReturnsSuspend`
  - `EvaluateDefaultChange_NewDefaultOutranksOverride_RestoresOverride`
  - `RankOf_ReturnsOneBasedRankOrNull`, `IsRelevant_TrueForRankedAndOverride`
  - `[Theory] PlanPersistedPick_CoversAllFourTransitions`, with the four rows from the rules above.

  In `DeviceSelectionStateTests.cs`, add:
  - `EvaluateDefaultChange_Snapshot_RestoreIdIsSelectionAtEvaluationTime`: select A, then evaluate with `isDeviceActive = id => { state.ClearSelection(); return true; }`. Expect `(Restore, "A")`. This pins the race fix: a clear that happens mid-evaluation can't produce a null restore ID.

- [ ] **Step 2: Run them to verify they fail.**
Run: `dotnet test AudioLeash.sln --filter "FullyQualifiedName~PriorityStateTests|FullyQualifiedName~DeviceSelectionStateTests"`
Expected: build errors (`PriorityState` undefined).

- [ ] **Step 3: Implement** `PriorityState.cs` and the `DeviceSelectionState` overload per the rules and Interfaces above.

- [ ] **Step 4: Run the full suite.**
Run: `dotnet test AudioLeash.sln`
Expected: all pass.

- [ ] **Step 5: Commit and push.**
```bash
git add AudioLeash/PriorityState.cs AudioLeash/DeviceSelectionState.cs AudioLeash.Tests/PriorityStateTests.cs AudioLeash.Tests/DeviceSelectionStateTests.cs
git commit -m "feat: add pure priority state machine and snapshot default-change evaluation"
git push
```

---

### Task 3: Pure list-editing logic for the settings window (`PriorityListEditor`)

**Files:**
- Create: `AudioLeash/PriorityListEditor.cs`
- Test: `AudioLeash.Tests/PriorityListEditorTests.cs`

**Interfaces:**
- Consumes: `RankedDevice` (Task 1).
- Produces:
  - `internal sealed record DeviceRow(string Id, string Name, string? Description, bool IsConnected);`
  - `internal sealed class PriorityListEditor`:
    - `PriorityListEditor(IReadOnlyList<RankedDevice> ranked, IEnumerable<DeviceRow> known)`
    - `IReadOnlyList<DeviceRow> Ranked { get; }` and `IReadOnlyList<DeviceRow> Unranked { get; }`. `Unranked` is always sorted by `Name` (ordinal, ignore case).
    - `bool MoveUp(string id)` and `bool MoveDown(string id)`. Each returns false when nothing moved.
    - `void MoveTo(string id, bool toRanked, int index)`: `index` is clamped. For unranked, the index is ignored because the list stays sorted.
    - `IReadOnlyList<RankedDevice> ToRankedList()`

**Rules:**
- **Merge.**
  - Ranked rows keep the persisted order.
  - A ranked device found in `known` takes the current Name, Description and IsConnected from there. One that isn't found keeps its persisted Name and Description, with `IsConnected = false`.
  - `Unranked` is every `known` device not in the ranked list.
  - Duplicate IDs in `known`: the first one wins.
- **Crossing the divider.**
  - `MoveDown` on the last ranked row unranks it.
  - `MoveUp` on any unranked row ranks it at the end of `Ranked`.
  - `MoveUp` on the first ranked row returns false. So does `MoveDown` on an unranked row.

- [ ] **Step 1: Write the failing tests.** One `[Fact]` each:
  - `Constructor_KeepsRankedOrderAndSortsUnranked`
  - `Constructor_RankedDeviceNotKnown_KeepsSlotWithPersistedNameAndDisconnected`
  - `Constructor_RankedDeviceKnown_UsesCurrentNameAndConnection`
  - `Constructor_DuplicateKnownIds_FirstWins`
  - `MoveUp_WithinRanked_SwapsWithPrevious`
  - `MoveUp_FirstRanked_ReturnsFalse`
  - `MoveDown_LastRanked_UnranksIt`
  - `MoveUp_UnrankedRow_AppendsToRanked`
  - `MoveDown_UnrankedRow_ReturnsFalse`
  - `MoveTo_UnrankedIntoRankedAtIndex_InsertsThere`
  - `MoveTo_RankedIntoUnranked_RemovesRankAndKeepsSorted`
  - `MoveTo_IndexOutOfRange_Clamps`
  - `ToRankedList_ReflectsCurrentOrderAndRefreshedNames`

- [ ] **Step 2: Run them to verify they fail.**
Run: `dotnet test AudioLeash.sln --filter "FullyQualifiedName~PriorityListEditorTests"`
Expected: build errors.

- [ ] **Step 3: Implement** `PriorityListEditor.cs` per the rules.

- [ ] **Step 4: Run the full suite.**
Run: `dotnet test AudioLeash.sln`
Expected: all pass.

- [ ] **Step 5: Commit and push.**
```bash
git add AudioLeash/PriorityListEditor.cs AudioLeash.Tests/PriorityListEditorTests.cs
git commit -m "feat: add priority list editor logic"
git push
```

---

### Task 4: Priority-mode engine in `AudioLeashContext`

Wire `PriorityState` into startup, device events, the debounced reconcile and default-change enforcement. Fix the legacy race. No tray UI changes yet: in this task, a manual pick still goes through the legacy path, and Task 5 changes that.

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs` (constructor, `OnDefaultDeviceChanged`, `OnDeviceStateChanged`, `ReconcileDeviceStateCore`, `Dispose`; new helpers)

**Interfaces:**
- Consumes: Task 1 (`Load*Priority`), Task 2 (`PriorityState`, `DefaultChangeDecision`, `PriorityDecision`).
- Produces (private members that Tasks 5 and 6 call):
  - fields `_playbackPriority` and `_capturePriority` (`PriorityState`)
  - `PriorityState PriorityFor(DataFlow flow)` and `DeviceSelectionState SelectionFor(DataFlow flow)`
  - `HashSet<string> GetActiveIds(DataFlow flow)` (disposes what it enumerates)
  - `void ApplyPriorityDecision(DataFlow flow, PriorityDecision decision, bool notify)`: switches the default to `decision.TargetId` when it isn't already the default, under `IsInternalChange`. Shows the balloon for the outcome when `notify` is true. Calls `UpdateTrayTooltip()` and `RefreshDeviceList()`.
  - `void SetDefaultIfNeeded(DataFlow flow, PriorityState state, string targetId)`: the shared "only if `GetCurrentDefaultId(flow) != targetId`" switch, wrapped in `IsInternalChange`.
  - `const int StartupGraceMs = 5000;`

- [ ] **Step 1: Startup.** For each flow, load its ranked list into its `PriorityState` via `SetRanked`.
  - **Priority mode:** if a saved pick exists, call `StartOverride(savedId)` and then `BeginStartupGrace()`. Next call `Initialize(GetActiveIds(flow))`, then `SetDefaultIfNeeded` silently when `TargetId` is non-null. Don't call `TryRestoreSavedDevice`, and leave the flow out of the "Saved Device Unavailable" balloon.
  - **Legacy mode:** unchanged.
  - **Grace timer:** if any flow began grace, start a one-shot `System.Windows.Forms.Timer` with `Interval = StartupGraceMs`. On tick, call `EndStartupGrace()` on both states, then `ReconcileDeviceState` for both flows. Dispose the timer in `Dispose`.

- [ ] **Step 2: Device events.**
  - In `OnDeviceStateChanged`, also arm the flow's debounce when that flow is in priority mode and `PriorityFor(flow).IsRelevant(deviceId)`.
  - In `ReconcileDeviceStateCore`, branch at the top. In priority mode, call `ApplyPriorityDecision(flow, PriorityFor(flow).EvaluateSettled(GetActiveIds(flow)), notify: true)`. Otherwise run the existing code.

- [ ] **Step 3: Default-change enforcement.** Restructure `OnDefaultDeviceChanged` so that both modes produce a `DefaultChangeDecision`:
  - priority mode via `PriorityFor(flow).EvaluateDefaultChange(newDefaultId, id => IsDeviceActive(flow, id))`
  - legacy via the new `DeviceSelectionState` overload

  The `Restore` branch uses `decision.RestoreId` (no `!` re-read of `SelectedDeviceId`). It sets and clears `IsInternalChange` on whichever state object the flow uses. `Suspend` arms the debounce, as today. Keep the existing `SafeInvoke` / `IsInternalChange` comments. In priority mode, the restore path skips `SetDeviceAvailability`, which is legacy-only.

- [ ] **Step 4: Notification copy** in `ApplyPriorityDecision`. Use `flowLabel` as today ("Audio Device" / "Recording Device"), and timeout 3000.

  | Outcome | Title | Text | Icon |
  |---|---|---|---|
  | `SwitchedUp` | `{flowLabel} Switched` | `Switched to {name} (higher priority)` | Info |
  | `FellBack` | `{flowLabel} Disconnected` | `{prevName} disconnected. Switched to {name}` | Warning |
  | `OverrideEnded` (target non-null) | `{flowLabel} Disconnected` | `{prevName} disconnected. Back to priority order: {name}` | Warning |
  | `OverrideEnded` (target null) / `NoneConnected` | `{flowLabel} Disconnected` | `{prevName} disconnected. No ranked devices are connected.` | Warning |
  | `ReassertSilently` / `None` | — | no balloon | — |

  Resolve names via `GetDeviceName(id)`, falling back to the `RankedDevice.Name` from `Ranked`, then to `"device"`. `OverrideEnded` must also persist the cleared pick: `SaveSelectedPlaybackDevice(null, null)` or `SaveSelectedCaptureDevice(null, null)`. Clear `_saved*DeviceName` to match.

- [ ] **Step 5: Verify build and tests.**
Run: `dotnet build AudioLeash.sln -c Release` and then `dotnet test AudioLeash.sln`
Expected: 0 warnings introduced, all tests pass.

- [ ] **Step 6: Manual smoke test (legacy unchanged).** Write a `settings.json` with no priority lists, run the app, and confirm that picking a device and switching it in Windows Sound settings restores it, exactly as before.

- [ ] **Step 7: Commit and push.**
```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "feat: enforce ranked device priority in AudioLeashContext"
git push
```

---

### Task 5: Tray menu, manual override and tooltip

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs` (`RefreshDeviceList`, `AddDeviceSection`, `DeviceMenuItem_Click`, `ClearSelection_Click`, `UpdateTrayTooltip`, `FormatDeviceTooltip`; add `ReturnToPriority_Click`)

**Interfaces:**
- Consumes: Task 4's members. The `Settings…` click handler `OpenSettings_Click` is added here as an empty stub, and Task 6 fills it.

- [ ] **Step 1: Device lines in priority mode.**
  - The label is the friendly name, plus `  #{rank}` when ranked, plus `  (manual)` when it is the override.
  - The `  (Windows default)` suffix works as today, but compares against `TargetId`.
  - `Checked` is true when the device is `TargetId`, or the override.
  - When the override isn't active (startup grace), show the existing grayed `{name}  (unavailable)` entry. Take its name from `_saved*DeviceName`.

- [ ] **Step 2: Manual pick in priority mode.**
  - `DeviceMenuItem_Click` calls `StartOverride(id)` and switches the default under `IsInternalChange`.
  - It persists via `SaveSelected*Device(id, name)`.
  - It shows the balloon title `Manual Device Selected`, text `{name}\n\nAudioLeash stays on it until you return to priority order or it disconnects.`
  - Legacy mode is unchanged.

- [ ] **Step 3: Action items.** These replace the single `Clear Selection` item:
  - `Return to priority order`: shown when any priority-mode flow has an override. For each such flow, call `ReturnToPriority(GetActiveIds(flow))`. Clear the persisted pick for that flow, then `SetDefaultIfNeeded` with the returned target when it's non-null. Then call `UpdateTrayTooltip()` and `RefreshDeviceList()`. No balloon.
  - `Clear Selection  (disable auto-restore)`: shown when any **legacy** flow exists, and enabled when one of those has a selection. It clears **only legacy flows**, through per-flow `SaveSelected*Device(null, null)`. Remove the `SaveSelectedDeviceIds(null, null)` call. Keep today's balloon.
  - `Settings…`: always shown, placed above `Start with Windows`.

- [ ] **Step 4: Tooltip.** In priority mode, a flow's tooltip name is `TargetId`'s name, with ` (manual)` when it's the override. If `TargetId` is null, show `{saved name} (waiting)` during grace, and omit the flow otherwise. Legacy mode is unchanged. Keep the existing 63-char clamp.

- [ ] **Step 5: Verify.**
Run: `dotnet build AudioLeash.sln -c Release` and then `dotnet test AudioLeash.sln`
Expected: all pass.

- [ ] **Step 6: Commit and push.**
```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "feat: show priorities and manual override in tray menu"
git push
```

---

### Task 6: Settings window, Save transitions and docs

**Files:**
- Create: `AudioLeash/SettingsForm.cs` (code-only WinForms, no designer file, which matches the project)
- Create: `AudioLeash/PriorityListControl.cs`
- Modify: `AudioLeash/AudioLeashContext.cs` (`OpenSettings_Click`, new `ApplySettings`), `AudioLeash/Program.cs`
- Modify: `README.md`, `DEVELOPMENT.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: Task 3 (`PriorityListEditor`, `DeviceRow`), Task 2 (`PlanPersistedPick`), and Task 4/5 members.
- Produces:
  - `internal sealed class PriorityListControl : UserControl`, with ctor `(PriorityListEditor editor)`. It renders `editor.Ranked`, then a non-selectable divider row, then `editor.Unranked`, in an owner-drawn `ListBox`:
    - rank number for ranked rows
    - connected dot (green when connected, grey when not)
    - name, with the description as a smaller second line
    - `not connected` / `connected` on the right for ranked rows

    It has ▲/▼ buttons that call `MoveUp`/`MoveDown` on the selected row and keep that row selected. Drag and drop calls `MoveTo`, computing `toRanked` from whether the drop point is above the divider row. Rebuild the items after every edit.
  - `internal sealed class SettingsForm : Form`:
    - ctor `(PriorityListEditor playback, PriorityListEditor capture, bool startWithWindows)`
    - title `AudioLeash Settings`
    - `TabControl` with `Playback`, `Recording` and `General`; the General tab holds a `Start with Windows` checkbox
    - `Save` (`DialogResult.OK`) and `Cancel` buttons; `AcceptButton` and `CancelButton` set
    - property `bool StartWithWindows`

- [ ] **Step 1: Theme.** In `Program.Main`, before `Application.Run`, call `Application.SetColorMode(SystemColorMode.System)` wrapped in `#pragma warning disable/restore WFO5001` (experimental since .NET 9). Run the app in Windows dark mode and confirm that the form, tabs and list are legible, and that the tray menu still uses `DarkMenuRenderer` (it sets its renderer explicitly). If the `TabControl` renders badly, keep `SetColorMode`, but owner-draw the tab headers using the `DarkMenuRenderer` palette when `WindowsTheme.IsDarkMode`. The list is owner-drawn anyway: use `SystemColors`, which `SetColorMode` remaps.

- [ ] **Step 2: Build `PriorityListControl` and `SettingsForm`** per the Interfaces. The form size is the builder's call, roughly 520×460 logical px with `AutoScaleMode.Dpi`. Put the divider text verbatim from Global Constraints.

- [ ] **Step 3: Wire `OpenSettings_Click`.**
  - If `_settingsForm` is open, `Activate()` it and return.
  - Otherwise build each editor from `PriorityFor(flow).Ranked` and the known devices: `EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Unplugged | DeviceState.Disabled)` mapped to `DeviceRow(d.ID, d.FriendlyName, d.DeviceFriendlyName, d.State == DeviceState.Active)`. Dispose each device.
  - Show the form modeless with `Show()`. On `FormClosed`, if `DialogResult == OK`, call `ApplySettings(form)`. Null out `_settingsForm` and dispose the form.

- [ ] **Step 4: Implement `ApplySettings`, per flow.**
  1. Take `newList = editor.ToRankedList()`, `wasPriority`, and `isPriority = newList.Count > 0`.
  2. Compute `pick = PriorityState.PlanPersistedPick(wasPriority, isPriority, SelectionFor(flow).SelectedDeviceId, PriorityFor(flow).OverrideId, GetCurrentDefaultId(flow))`.
  3. Call `Save*Priority(newList)` and `PriorityFor(flow).SetRanked(newList)`.
  4. Persist the pick with `SaveSelected*Device(pick, GetDeviceName(pick))`, and update `_saved*DeviceName`.
  5. Then, by mode:
     - **Entering or staying in priority mode:** `SelectionFor(flow).ClearSelection()`. When entering, there's no override, because `pick` is null.
     - **Leaving priority mode:** call `ReturnToPriority(empty)` to drop the override. If `pick` is non-null, call `SelectionFor(flow).SelectDevice(pick)`.
  6. If now in priority mode: `ApplyPriorityDecision(flow, PriorityFor(flow).EvaluateSettled(GetActiveIds(flow)), notify: true)`.

  After both flows, apply `StartWithWindows` through `_startupService.Enable`/`Disable` when it changed (same try/catch as `StartupItem_Click`). Then call `UpdateTrayTooltip()` and `RefreshDeviceList()`.

- [ ] **Step 5: Verify build and tests.**
Run: `dotnet build AudioLeash.sln -c Release` and then `dotnet test AudioLeash.sln`
Expected: all pass.

- [ ] **Step 6: Manual verification on real hardware.** Run the app and check each scenario:
  1. **Ranked playback list:** Headphones #1, Speakers #2. Save, and the default goes to the headphones.
  2. **Reorder:** move Speakers above Headphones, then Save. Audio switches to the speakers with a `Switched` balloon.
  3. **Manual pick:** pick an unranked device in the tray. Unplug and replug a ranked device; the pick holds. Then choose `Return to priority order`, and audio goes back to the top ranked device.
  4. **Restart:** pick a device manually, then restart AudioLeash. The pick is kept, and the tray shows `(manual)`.
  5. **Override disconnects:** with a manual pick on a USB device, unplug it. You get one `Back to priority order` balloon.
  6. **Emptying the list:** clear the list and Save. The current device becomes the legacy pick, and `Clear Selection` is back.
  7. **Theme:** the settings window renders correctly in both light and dark mode.
  8. **Single window:** with the settings window open, choosing `Settings…` again brings the existing window to the front.

  Report any scenario you couldn't exercise (no HDMI device, say) instead of claiming it passed.

- [ ] **Step 7: Docs.**
  - `README.md`: in "Using AudioLeash", add a "Device priority" subsection covering ranking, manual picks and Return to priority order. Also update "Features at a glance".
  - `DEVELOPMENT.md`: in Project Structure, add `RankedDevice.cs`, `PriorityState.cs`, `PriorityListEditor.cs`, `PriorityListControl.cs`, `SettingsForm.cs` and the new test files. Add a short Architecture Notes entry on legacy versus priority mode and the override persisted in the `Selected*` fields. Add "device priority" to "Already shipped". Remove the race from Known Issues if it's listed there.
  - `CLAUDE.md`: add the new files to the Project Structure tree, and delete the **Known race condition** bullet (fixed in Task 2/4).

- [ ] **Step 8: Commit and push.**
```bash
git add AudioLeash/SettingsForm.cs AudioLeash/PriorityListControl.cs AudioLeash/AudioLeashContext.cs AudioLeash/Program.cs README.md DEVELOPMENT.md CLAUDE.md
git commit -m "feat: add settings window for ranking devices"
git push
```

## Post-review changes

- Task 6 specified `notify: true` when applying the priority decision on Save. Overruled after final review: the outcome balloons misdescribed list edits (for example "higher priority" when nothing switched). Save now applies the decision silently and shows one neutral "Now using X (priority order)" balloon only if the Windows default actually changed.
- The ranked list and the persisted pick are saved in one write (`SettingsService.SavePlaybackFlow` / `SaveCaptureFlow`) instead of two, so a partial write cannot resurrect a stale legacy pick.
