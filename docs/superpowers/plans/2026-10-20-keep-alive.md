# Keep Alive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A per-playback-device "keep alive" option. It plays digital silence (Zero mode) to the current default playback device when that device has the option on, and pauses while the session is locked, the display is off, or the system is suspended.

**Architecture:**
- **`KeepAliveState`** is the pure, tested decision logic that picks which device, if any, should be streaming.
- **`KeepAliveStream`** is a thin NAudio `WasapiOut` wrapper that plays a zero-filling `SilenceWaveProvider`.
- **`KeepAlivePauseMonitor`** is a hidden window that turns lock, display and suspend messages into signals.
- **`KeepAliveRows`** is the pure model behind a new "Keep devices awake" sub-page in `SettingsForm`.
- **`AudioLeashContext`** wires everything together on the UI thread.

**Tech Stack:** C# 14 / .NET 10 WinForms, NAudio.Wasapi 2.2.1, xUnit + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-10-20-keep-alive-design.md`

## Global Constraints

- `net10.0-windows`, `#nullable enable` at the top of every new file (match the existing files), file-scoped `namespace AudioLeash;`.
- No new NuGet packages. `NAudio.Wasapi` 2.2.1 is already referenced.
- Playback (`DataFlow.Render`) only, Multimedia role only (`Role.Multimedia`).
- Zero mode only, with no mode UI. The silence source sits behind `IWaveProvider` so a later Fluctuate mode only swaps the source.
- Keep-alive failures are silent: no balloons, no `MessageBox`. `System.Diagnostics.Debug.WriteLine` is fine.
- Settings JSON field name: `keepAliveDevices` (camelCase from `KeepAliveDevices`). Each item has the `RankedDevice` shape.
- No timers or threads exist while keep-alive isn't wanted. The retry timer runs only while a stream is wanted but not running.
- Dispose every `MMDevice`, `MMDeviceEnumerator`, `WasapiOut`, timer and native registration.
- All tests: `dotnet test AudioLeash.sln`. All must pass before each commit.
- Commits use conventional prefixes and end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Work on `feature/keep-alive`, push after each commit (CLAUDE.md: never leave unpushed changes).

## Review Focus

1. **The default device switches while keep-alive is streaming.** For example, AudioLeash restores the default, or an HDMI flap bounces it. Expected: the stream stops on the old device and starts on the new one only if the new device has keep-alive on, with no stream leaked and no churn during a flap. → Task 1 tests `Plan_DefaultMovesToDisabledDevice_Stops` and `Plan_DefaultMovesToOtherEnabledDevice_Restarts`. Task 7 coalesces with a 500 ms one-shot timer.
2. **An exclusive-mode app or an audio service restart kills the stream.** Expected: it comes back by itself once possible, without a restart loop that burns CPU. → Task 7 routes a fault to a 5 s retry timer, not an immediate restart. Task 5's `Faulted` fires only for unrequested stops.
3. **The PC wakes from sleep with a different default, or none.** Expected: keep-alive re-checks the default on resume. → Task 4 test `PowerBroadcast_ResumeAutomatic_RaisesResumed`. Task 7 re-queries the default on `Resumed`.
4. **A device that's enabled but missing from Windows still appears on the page and can be switched off.** → Task 3 test `Build_EnabledDeviceUnknownToWindows_IsListedDisconnectedAndEnabled`.
5. **Opening the sub-page and going back must not apply or lose pending priority edits.** → Task 6 manual check, step 4.

---

### Task 1: `KeepAliveState` (pure decision logic)

**Files:**
- Create: `AudioLeash/KeepAliveState.cs`
- Test: `AudioLeash.Tests/KeepAliveStateTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal readonly record struct KeepAlivePlan(bool StopCurrent, string? StartId);
  internal sealed class KeepAliveState
  {
      public KeepAliveState(IReadOnlyList<RankedDevice> devices);   // duplicates/empty ids ignored, first wins
      public IReadOnlyList<RankedDevice> Devices { get; }           // enabled devices, insertion order
      public bool IsEnabled(string id);
      public bool SetEnabled(RankedDevice device, bool enabled);    // true if changed; enabling an enabled id refreshes nothing, returns false
      public string? DefaultId { get; set; }                        // current Render/Multimedia default (always Active by definition), null if none
      public bool IsLocked { get; set; }
      public bool IsDisplayOff { get; set; }
      public bool IsSuspended { get; set; }
      public string? DesiredId { get; }                             // DefaultId if enabled and no pause flag, else null
      public KeepAlivePlan Plan(string? runningId);                 // what to do given the device currently streaming
  }
  ```
  `Plan` rules: if `runningId == DesiredId` (ordinal) → `(false, null)`. Otherwise → `(StopCurrent: runningId is not null, StartId: DesiredId)`.

- [ ] **Step 1: Write the failing tests** in `KeepAliveStateTests.cs`. Use the helper `static RankedDevice D(string id) => new(id, id + " name");`.
  - `DesiredId_DefaultEnabledAndNoPause_IsDefault`: devices [A], DefaultId "A" → `"A"`.
  - `DesiredId_DefaultNotEnabled_IsNull`: devices [A], DefaultId "B" → null.
  - `DesiredId_NoDefault_IsNull`.
  - `[Theory]` `DesiredId_AnyPauseFlag_IsNull` with each of locked / displayOff / suspended set alone → null.
  - `DesiredId_AllPauseFlagsCleared_ResumesDefault`: set all three, then clear all three → `"A"`.
  - `Plan_NothingRunningNothingWanted_DoesNothing` → `(false, null)`.
  - `Plan_NothingRunning_StartsDesired` → `(false, "A")`.
  - `Plan_AlreadyRunningDesired_DoesNothing`: `Plan("A")` with desired "A" → `(false, null)`.
  - `Plan_DefaultMovesToDisabledDevice_Stops`: running "A", DefaultId "B" (not enabled) → `(true, null)`.
  - `Plan_DefaultMovesToOtherEnabledDevice_Restarts`: devices [A, B], running "A", DefaultId "B" → `(true, "B")`.
  - `Plan_LockedWhileRunning_Stops`: running "A", IsLocked → `(true, null)`.
  - `SetEnabled_TogglingDefaultOff_PlanStops`: running "A", `SetEnabled(D("A"), false)` returns true → `Plan("A") == (true, null)`.
  - `SetEnabled_Duplicate_ReturnsFalseAndKeepsOneEntry`.
  - `Constructor_DropsDuplicateAndEmptyIds`: `[D("A"), D("A"), new("", "x")]` → `Devices.Count == 1`.

- [ ] **Step 2: Run them and confirm they fail**
  Run: `dotnet test AudioLeash.sln --filter KeepAliveStateTests`
  Expected: build error, `KeepAliveState` not defined.

- [ ] **Step 3: Implement `KeepAliveState` and `KeepAlivePlan`** in `AudioLeash/KeepAliveState.cs` exactly as in Interfaces. Back the devices with a `List<RankedDevice>` plus ordinal id lookups. Use a class doc comment in the style of `PriorityState.cs`.

- [ ] **Step 4: Run them and confirm they pass**
  Run: `dotnet test AudioLeash.sln --filter KeepAliveStateTests`
  Expected: all pass.

- [ ] **Step 5: Commit**: `feat: add keep-alive decision state`

---

### Task 2: Persist keep-alive devices

**Files:**
- Modify: `AudioLeash/SettingsService.cs` (add a section after "Device priority lists" and add the record field)
- Test: `AudioLeash.Tests/SettingsServiceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public IReadOnlyList<RankedDevice> LoadKeepAliveDevices();          // sanitized via existing Sanitize; empty if absent
  public void SaveKeepAliveDevices(IReadOnlyList<RankedDevice> list); // preserves all other fields
  ```
  Add `IReadOnlyList<RankedDevice>? KeepAliveDevices = null` as the last parameter of `AppSettings`.

- [ ] **Step 1: Write the failing tests** in `SettingsServiceTests.cs`:
  - `LoadKeepAliveDevices_WhenFileAbsent_ReturnsEmpty`.
  - `KeepAliveDevices_RoundTrip`: save `[new("id1","HDMI","NVIDIA")]` → load equals it (record equality).
  - `SaveKeepAliveDevices_PreservesPriorityAndSelection`: save the playback priority and the selected playback device, then save keep-alive → the priority and selection still load.
  - `SavePlaybackFlow_PreservesKeepAliveDevices`: the reverse direction.
  - `LoadKeepAliveDevices_FromOldFileWithoutField_ReturnsEmpty`: write `{"selectedPlaybackDeviceId":"x"}` to `settings.json` directly → empty.
  - `LoadKeepAliveDevices_DropsDuplicatesAndEmptyIds`.

- [ ] **Step 2: Run them and confirm they fail**: `dotnet test AudioLeash.sln --filter SettingsServiceTests`. Expected: compile error.

- [ ] **Step 3: Implement** the two methods following the pattern of `LoadPlaybackPriority` and `SavePlaybackPriority`.

- [ ] **Step 4: Run them and confirm they pass**: `dotnet test AudioLeash.sln --filter SettingsServiceTests`. Expected: all pass.

- [ ] **Step 5: Commit**: `feat: persist keep-alive devices in settings`

---

### Task 3: `KeepAliveRows` (pure model for the sub-page)

**Files:**
- Create: `AudioLeash/KeepAliveRows.cs`
- Test: `AudioLeash.Tests/KeepAliveRowsTests.cs`

**Interfaces:**
- Consumes: `DeviceRow(string Id, string Name, string? Description, bool IsConnected)` from `PriorityListEditor.cs`, and `RankedDevice`.
- Produces:
  ```csharp
  internal sealed record KeepAliveRow(DeviceRow Device, bool Enabled);
  internal static class KeepAliveRows
  {
      // Every known device, plus enabled ids Windows no longer lists (IsConnected: false, saved name/description).
      // Sorted by Name (StringComparer.CurrentCultureIgnoreCase), then Id ordinal. Known rows win over saved ones for name.
      public static IReadOnlyList<KeepAliveRow> Build(IEnumerable<DeviceRow> known, IReadOnlyList<RankedDevice> enabled);
      // "Off" / "On for 1 device" / "On for {n} devices"
      public static string Summary(int enabledCount);
  }
  ```

- [ ] **Step 1: Write the failing tests**:
  - `Build_MarksEnabledDevices`: known [A, B], enabled [A] → A Enabled, B not.
  - `Build_EnabledDeviceUnknownToWindows_IsListedDisconnectedAndEnabled`: known [], enabled `[new("X","Old AVR","NVIDIA")]` → one row `("X","Old AVR","NVIDIA", IsConnected:false)`, Enabled.
  - `Build_SortsByNameCaseInsensitive`: names "b", "A", "c" → A, b, c.
  - `Build_DuplicateKnownIds_FirstWins`.
  - `[Theory]` `Summary_Formats`: 0→"Off", 1→"On for 1 device", 2→"On for 2 devices".

- [ ] **Step 2: Run them and confirm they fail**: `dotnet test AudioLeash.sln --filter KeepAliveRowsTests`.

- [ ] **Step 3: Implement** `KeepAliveRows`.

- [ ] **Step 4: Run them and confirm they pass**.

- [ ] **Step 5: Commit**: `feat: add keep-alive settings row model`

---

### Task 4: `KeepAlivePauseMonitor` (lock / display / suspend signals)

**Files:**
- Create: `AudioLeash/KeepAlivePauseMonitor.cs`
- Test: `AudioLeash.Tests/KeepAlivePauseMonitorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal enum PauseSignal { Locked, Unlocked, DisplayOff, DisplayOn, Suspending, Resumed }
  internal sealed class KeepAlivePauseMonitor : NativeWindow, IDisposable
  {
      public KeepAlivePauseMonitor(Action<PauseSignal> onSignal); // callback runs on the creating (UI) thread
  }
  ```
- Pinned Win32 values:
  - `WM_WTSSESSION_CHANGE = 0x02B1`: wParam `WTS_SESSION_LOCK = 0x7` → `Locked`, `WTS_SESSION_UNLOCK = 0x8` → `Unlocked`.
  - `WM_POWERBROADCAST = 0x0218`: wParam `PBT_APMSUSPEND = 0x4` → `Suspending`. `PBT_APMRESUMESUSPEND = 0x7` and `PBT_APMRESUMEAUTOMATIC = 0x12` → `Resumed`. `PBT_POWERSETTINGCHANGE = 0x8013`: lParam points at `POWERBROADCAST_SETTING { Guid PowerSetting; uint DataLength; byte Data[] }`. If `PowerSetting == GUID_CONSOLE_DISPLAY_STATE {6FE69556-704A-47A0-8F24-C28D936FDA47}`, read the `uint` at offset 20: `0` → `DisplayOff`, `1` or `2` (dimmed) → `DisplayOn`. Return `(IntPtr)1` for `WM_POWERBROADCAST`.
- Registration in the constructor, after `CreateHandle(new CreateParams { Caption = "AudioLeash keep-alive" })` (top-level, hidden, same as `SessionEndListener`):
  - `wtsapi32!WTSRegisterSessionNotification(Handle, NOTIFY_FOR_THIS_SESSION = 0)`
  - `user32!RegisterPowerSettingNotification(Handle, ref GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE = 0)`, keeping the returned handle.
  - Check return values. On failure, `Debug.WriteLine` the `Marshal.GetLastWin32Error()` and carry on. Windows sends the current display state straight after registration.
  - `Dispose`: `UnregisterPowerSettingNotification`, `WTSUnRegisterSessionNotification`, `DestroyHandle`.

- [ ] **Step 1: Write the failing tests.** Follow `SessionEndListenerTests`: `SendMessage` to `monitor.Handle`, collect signals into a `List<PauseSignal>`, and dispose in `Dispose()`. For display tests, build the struct in unmanaged memory: `Marshal.AllocHGlobal(24)`, write the Guid bytes at 0, `DataLength = 4` at 16, and the value at 20. Free it in `finally`.
  - `SessionLock_RaisesLocked`, `SessionUnlock_RaisesUnlocked`
  - `PowerBroadcast_Suspend_RaisesSuspending`, `PowerBroadcast_ResumeAutomatic_RaisesResumed`
  - `DisplayState_Off_RaisesDisplayOff`, `DisplayState_On_RaisesDisplayOn`, `DisplayState_Dimmed_RaisesDisplayOn`
  - `PowerSettingChange_OtherGuid_RaisesNothing`
  - `PowerBroadcast_ReturnsTrue`
  - Filter the collected list to the signal under test, or clear it after construction. Real registration may deliver a genuine `DisplayOn` first.

- [ ] **Step 2: Run them and confirm they fail**: `dotnet test AudioLeash.sln --filter KeepAlivePauseMonitorTests`.

- [ ] **Step 3: Implement** `KeepAlivePauseMonitor`. Use the same doc-comment style as `SessionEndListener`, explaining why a window is needed (these notifications target an HWND).

- [ ] **Step 4: Run them and confirm they pass**.

- [ ] **Step 5: Commit**: `feat: detect lock, display-off and suspend for keep-alive`

---

### Task 5: `SilenceWaveProvider` and `KeepAliveStream`

**Files:**
- Create: `AudioLeash/SilenceWaveProvider.cs`
- Create: `AudioLeash/KeepAliveStream.cs`
- Test: `AudioLeash.Tests/SilenceWaveProviderTests.cs` (the stream itself needs the real audio stack, so it isn't unit-tested)

**Interfaces:**
- Produces:
  ```csharp
  internal sealed class SilenceWaveProvider(WaveFormat format) : IWaveProvider
  {
      public WaveFormat WaveFormat { get; }
      public int Read(byte[] buffer, int offset, int count); // clears [offset, offset+count), returns count; no allocation
  }
  internal sealed class KeepAliveStream : IDisposable
  {
      public static KeepAliveStream? TryStart(string deviceId); // null on any failure (logged via Debug.WriteLine)
      public string DeviceId { get; }
      public event EventHandler? Faulted;                        // stream stopped without Dispose being called; raised on the playback thread
      public void Dispose();                                     // stops (joins the playback thread) and releases everything; idempotent
  }
  ```
- Pinned NAudio 2.2.1 facts, checked against the v2.2.1 `WasapiOut.cs` source:
  - Constructor: `new WasapiOut(MMDevice device, AudioClientShareMode.Shared, useEventSync: false, latency: 400)`.
    - **Use polling, not event sync.** In shared mode, event sync passes periodicity 0 and wakes every engine period (~10 ms). Polling sleeps `latency / 2`, which works out to about 5 wakeups a second.
  - The format is the device's mix format: `using var probe = device.AudioClient; var format = probe.MixFormat;`. All-zero bytes are silence for both PCM and IEEE float, so no conversion is needed.
  - `PlaybackStopped` is posted through the `SynchronizationContext` captured in the `WasapiOut` constructor, or raised directly on the playback thread if there is none. `Stop()`/`Dispose()` **join the playback thread**.
- **Threading (pinned):** `TryStart` does all COM work on a thread-pool (MTA) thread: `Task.Run(() => …).GetAwaiter().GetResult()`. That work is a new `MMDeviceEnumerator`, `GetDevice(deviceId)`, `MixFormat`, `new WasapiOut`, `Init(new SilenceWaveProvider(format))` and `Play()`. Doing it there means no WinForms `SynchronizationContext` is captured and no STA COM object crosses apartments. The stream owns and disposes its own enumerator and device.
- `Faulted` is raised from the `PlaybackStopped` handler only when `Dispose` hasn't been requested (a `volatile bool _disposing`). Subscribers **must not** call `Dispose` synchronously from the handler, because it would join the playback thread from itself. Task 7 uses `BeginInvoke`.

- [ ] **Step 1: Write the failing tests**:
  - `Read_ClearsRequestedRangeAndReturnsCount`: buffer filled with `0xAB`, `Read(buf, 4, 8)` → returns 8, bytes 4..11 are 0, and 0..3 and 12.. are still `0xAB`.
  - `WaveFormat_IsTheOneGiven`: `WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)` → same instance.

- [ ] **Step 2: Run them and confirm they fail**: `dotnet test AudioLeash.sln --filter SilenceWaveProviderTests`.

- [ ] **Step 3: Implement** `SilenceWaveProvider` (`buffer.AsSpan(offset, count).Clear()`), then `KeepAliveStream` as pinned above.

- [ ] **Step 4: Run the tests and build**: `dotnet test AudioLeash.sln`. Expected: all pass, with no new warnings in the build output.

- [ ] **Step 5: Commit**: `feat: add silent keep-alive render stream`

---

### Task 6: Settings window: "Keep devices awake" card and sub-page

**Files:**
- Modify: `AudioLeash/SettingsForm.cs`

**Interfaces:**
- Consumes: `KeepAliveRow`, `KeepAliveRows.Summary` (Task 3), `ToggleSwitch`, `SettingsTheme`.
- Produces:
  ```csharp
  internal sealed class KeepAliveToggledEventArgs(DeviceRow device, bool enabled) : EventArgs
  { public DeviceRow Device { get; } = device; public bool Enabled { get; } = enabled; }

  // SettingsForm constructor gains a parameter (after startWithWindows):
  public SettingsForm(PriorityListEditor playback, PriorityListEditor capture, bool startWithWindows,
                      IReadOnlyList<KeepAliveRow> keepAlive, SettingsTheme? theme = null);
  public event EventHandler<KeepAliveToggledEventArgs>? KeepAliveToggled; // raised immediately on a user flip
  ```
- Layout:
  - **Main page.** Add a "Keep devices awake" card as a new `content` row between `pages` and `startupCard` (RowCount 5). Build it the same way as `startupCard`: title, subtitle `KeepAliveRows.Summary(n)`, and a right-hand `›` label in place of the switch.
    - Pull the card construction into a private helper that both cards use, so the styling isn't duplicated.
    - The keep-alive card has spacing between it and Start with Windows (top margin 12, or 8 between the two cards; whichever looks right).
    - The card is focusable (`TabStop`, accessible role `PushButton`, `AccessibleName = "Keep devices awake"`). Click, Enter or Space opens the sub-page.
  - **Sub-page.** A second `TableLayoutPanel` (Dock Fill, hidden by default) is a sibling of `content` in `Controls`. Its rows are:
    1. A header row: a flat back button `‹` (`AccessibleName = "Back"`) and a title label "Keep devices awake" in a larger font.
    2. A muted hint, verbatim: `Plays inaudible silence to these devices while they're the default, so receivers and TVs don't cut off the start of sounds. Pauses while your PC is locked or the screen is off.`
    3. A scrollable panel (`AutoScroll`, `theme.Surface` background) with one row per `KeepAliveRow`: the name, a muted second line of `Description`, with ` · not connected` appended when `!IsConnected`, and a `ToggleSwitch` with `AccessibleName = row name`. Clicking the row text flips the switch, as with `startupCard`.
  - Flipping a switch raises `KeepAliveToggled` and updates the card subtitle count.
  - Switching pages toggles `Visible` on the two panels and moves focus: to the first switch (or the back button) on the way in, and to the card on the way back.
- `ProcessCmdKey`: while the sub-page is visible, `Esc` goes back and the Ctrl+Tab family returns `true` without acting. Existing behaviour is unchanged on the main page.
- Pending priority edits live in the `PriorityListEditor`s and apply on `FormClosed`, so page switching doesn't touch them. Don't add any apply call.

- [ ] **Step 1: Implement** the above. Update the `SettingsForm` class doc comment.
- [ ] **Step 2: Update the only constructor call site**, `AudioLeashContext.OpenSettings_Click`.
  - Pass `KeepAliveRows.Build(EnumerateKnownDevices(DataFlow.Render), _settingsService.LoadKeepAliveDevices())`. Task 7 switches this to `_keepAlive.Devices`.
  - Create `private List<DeviceRow> EnumerateKnownDevices(DataFlow flow)` by pulling the `known` list building out of `CreatePriorityEditor`, which then calls it.
  - Don't subscribe to `KeepAliveToggled` yet. Task 7 does that.
- [ ] **Step 3: Build and test**: `dotnet build AudioLeash.sln -c Release && dotnet test AudioLeash.sln`. Expected: success, all pass.
- [ ] **Step 4: Manual check** (run `AudioLeash\bin\Release\net10.0-windows\AudioLeash.exe`, after exiting any running copy):
  - In both the light and dark Windows themes, the card shows "Off".
  - Clicking it opens the sub-page, which lists the playback devices, including unplugged ones, as "not connected".
  - Flipping a switch updates the count after going back.
  - Esc goes back first, then closes.
  - Tab and Space reach and flip the switches.
  - **Drag a priority row, open the sub-page, go back:** the drag is still there and the default device hasn't changed until the window closes.
- [ ] **Step 5: Commit**: `feat: add keep devices awake page to settings`

---

### Task 7: Wire keep-alive into `AudioLeashContext`

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–6.
- New fields:
  - `KeepAliveState _keepAlive`, created from `_settingsService.LoadKeepAliveDevices()`. Initialise it after `_settingsService`.
  - `KeepAliveStream? _keepAliveStream`.
  - `KeepAlivePauseMonitor _pauseMonitor`, created next to `_sessionEndListener`, with callback `OnPauseSignal`.
  - `const int KeepAliveCoalesceMs = 500;` and `const int KeepAliveRetryMs = 5000;`.
  - `System.Windows.Forms.Timer _keepAliveCoalesce` (one-shot: Tick → Stop → `RefreshKeepAliveDefault()`).
  - `System.Windows.Forms.Timer _keepAliveRetry` (one-shot: Tick → Stop → `ReconcileKeepAlive()`).
- New methods (all UI thread):
  - `void ArmKeepAliveCoalesce()`: restarts `_keepAliveCoalesce`. When called from the audio thread, wrap it in `SafeInvoke`, as `ArmDebounce` does.
  - `void RefreshKeepAliveDefault()`: `_keepAlive.DefaultId = GetCurrentDefaultId(DataFlow.Render); ReconcileKeepAlive();`.
  - `void ReconcileKeepAlive()`:
    1. If `_shuttingDown`, return.
    2. `var plan = _keepAlive.Plan(_keepAliveStream?.DeviceId)`. If `StopCurrent`, dispose and null the stream.
    3. If `StartId` is set, `TryStart` it. On success, store the stream, subscribe `Faulted`, and stop the retry timer. On failure, start the retry timer.
    4. If `_keepAlive.DesiredId is null`, stop the retry timer.
  - `void OnKeepAliveFaulted(object? s, EventArgs e)`: playback thread. Do `if (_shuttingDown) return;`, then `_contextMenu.BeginInvoke(...)` in `try/catch (ObjectDisposedException or InvalidOperationException)`.
    - **Not `SafeInvoke`, which is synchronous:** the UI side disposes the stream, which joins this thread.
    - The UI side does: if `_keepAliveStream == sender`, dispose and null it, then start `_keepAliveRetry`. Don't restart immediately, so a device that faults straight away can't spin.
  - `void OnPauseSignal(PauseSignal s)`:
    - `Locked` / `Unlocked` → `IsLocked`.
    - `DisplayOff` / `DisplayOn` → `IsDisplayOff`.
    - `Suspending` → `IsSuspended = true`.
    - `Resumed` → `IsSuspended = false`, then `ArmKeepAliveCoalesce()`, because the default may have changed during sleep.
    - Finish with `ReconcileKeepAlive()`.
  - `void OnKeepAliveToggled(object? s, KeepAliveToggledEventArgs e)`:
    - `if (_keepAlive.SetEnabled(new RankedDevice(e.Device.Id, e.Device.Name, e.Device.Description), e.Enabled))`, then `_settingsService.SaveKeepAliveDevices(_keepAlive.Devices); ReconcileKeepAlive();`.
- Hooks:
  - Constructor end, after `RefreshDeviceList()`: `RefreshKeepAliveDefault();`. When nothing is enabled, this doesn't start a stream and leaves no timer running.
  - `OnDefaultDeviceChanged`: as the **first** statement, `if (flow == DataFlow.Render && _keepAlive.Devices.Count > 0) ArmKeepAliveCoalesce();`. Keep it before `NoAction` returns. The tick re-queries the default, so `newDefaultId` isn't needed.
  - `OnDeviceStateChanged`: `if (_keepAlive.IsEnabled(deviceId)) ArmKeepAliveCoalesce();`.
  - `OpenSettings_Click`: subscribe `form.KeepAliveToggled += OnKeepAliveToggled;`. Replace the Task 6 stub.
  - `Dispose`: after `_shuttingDown = true`:
    - stop and dispose both keep-alive timers;
    - dispose `_keepAliveStream`;
    - dispose `_pauseMonitor` next to `_sessionEndListener`.
    - Do the stream dispose **before** `_contextMenu.Dispose()`, so a pending fault `BeginInvoke` finds the stream already nulled or disposed. `Dispose` is idempotent.

- [ ] **Step 1: Implement** the above. Add doc comments matching the surrounding density (why `BeginInvoke`, why coalesce).
- [ ] **Step 2: Build and test**: `dotnet build AudioLeash.sln -c Release && dotnet test AudioLeash.sln`. Expected: success, all pass.
- [ ] **Step 3: Manual verification on this machine.** Report what you observe for each check. Don't claim a pass for anything you couldn't observe.
  1. Enable keep-alive for the current default device. The Windows volume mixer shows AudioLeash. Running `powercfg /requests` (admin) lists an audio stream under SYSTEM.
  2. Switch the default to a device without keep-alive (via Windows sound settings, with AudioLeash in legacy mode with no pick, or through the tray). The mixer entry disappears within about 1 s.
  3. Lock with Win+L, unlock, then check `powercfg /requests`. The stream is gone while locked and back after unlock. (Run the check from a logged-in console afterwards, or add a temporary `Debug.WriteLine` and use DebugView.)
  4. Task Manager / Process Explorer with the stream running for 5 minutes: CPU stays at about 0%, and private bytes stay flat (record the start and end values).
  5. Disable keep-alive. AudioLeash shows no extra threads beyond the baseline (Process Explorer Threads tab, before and after).
- [ ] **Step 4: Commit**: `feat: keep the default playback device awake with silence`

---

### Task 8: Docs and version

**Files:**
- Modify: `README.md`: a "### Keep devices awake" subsection under "Using AudioLeash", after "Device priority", and a line in "Features at a glance".
- Modify: `DEVELOPMENT.md`: Project Structure (new files), Architecture Notes (a keep-alive bullet: polling `WasapiOut` at 400 ms latency, MTA init, `BeginInvoke` on fault, retry timer, pause monitor), and Roadmap (add "Fluctuate mode for keep-alive (tiny non-zero samples, for hardware that ignores pure digital silence)").
- Modify: `CLAUDE.md`: the project-structure tree. Add `KeepAliveState.cs`, `KeepAliveRows.cs`, `KeepAliveStream.cs`, `SilenceWaveProvider.cs`, `KeepAlivePauseMonitor.cs` and the four new test files, with one-line comments in the existing style.
- Modify: `AudioLeash/AudioLeash.csproj`: `<Version>2.0.0</Version>` → `<Version>2.1.0</Version>`.
- Modify: `RELEASE_NOTES.md`: a new `## v2.1.0` section above v2.0.0, in the same style.

README content (the wording is yours; these facts are required):
- What it's for: HDMI/S/PDIF receivers and TVs that cut off the start of sounds after a few seconds of quiet (e.g. Dolby Atmos over HDMI).
- How to turn it on: Settings… → **Keep devices awake** → switch it on per device. It applies immediately.
- It only runs while that device is the Windows default. It plays pure digital silence, so it's inaudible.
- It pauses while the PC is locked, the screen is off, or the PC is asleep.
- You'll see AudioLeash in the volume mixer while it's active.
- **Limitation:** while it's active, Windows treats audio as playing, so automatic sleep won't happen while you're unlocked and the screen is on. Set the screen to turn off before the PC sleeps, or lock the PC when you leave.
- Some hardware ignores pure silence. A fluctuating mode may come later.

- [ ] **Step 1: Make the edits.**
- [ ] **Step 2: Build and test**: `dotnet build AudioLeash.sln -c Release && dotnet test AudioLeash.sln`. Expected: success, all pass.
- [ ] **Step 3: Commit**: `docs: document keep devices awake and bump version to 2.1.0`
