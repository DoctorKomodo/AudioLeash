# AudioLeash — Development

Developer-facing documentation: building, testing, packaging, architecture, and the roadmap. For install and usage, see [README.md](README.md).

## Repository Hosting

The primary repository is a self-hosted Forgejo instance (`origin`). It is push-mirrored to [GitHub](https://github.com/DoctorKomodo/AudioLeash), which also hosts the Releases page. Push to Forgejo only; do not commit directly on GitHub, as the next mirror sync overwrites it.

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# 14 (.NET 10) |
| UI | Windows Forms — system tray only |
| Audio API | NAudio.Wasapi 2.2.1 (`MMDeviceEnumerator`, `IMMNotificationClient`) |
| Default device | `PolicyConfigClient.cs` — self-contained COM interop |
| Installer | Inno Setup 6 |
| Target OS | Windows 10 / 11 |

## Building

```powershell
dotnet build AudioLeash.sln -c Release
```

Run the build (no window appears — it lives in the tray):

```powershell
AudioLeash\bin\Release\net10.0-windows\AudioLeash.exe
```

## Running Tests

```powershell
dotnet test AudioLeash.sln
```

Tests use **xUnit** + **NSubstitute** and live in `AudioLeash.Tests/`. They cover the pure logic — the device-selection state machine, settings persistence, startup registration, and theme detection — and deliberately avoid anything that needs a live Windows audio stack.

## Regenerating the Icon

`AudioLeash/Resources/icon.ico` is generated, not hand-drawn. The source of truth is `tools/generate-icon.py`.

```powershell
python -m pip install Pillow            # dev-time only; not a build dependency
python tools/generate-icon.py           # rewrites AudioLeash/Resources/icon.ico
python tools/generate-icon.py --preview # also dumps PNGs to build/icon-preview/
```

The script renders nine frames (16, 20, 24, 32, 40, 48, 64, 128, 256); 20, 24 and 40 are the tray icon at 125%, 150% and 250% scaling, since the app is per-monitor DPI aware. Frames up to 32px are drawn from **simplified geometry** — a square 1px-wall hook instead of the curved stroked one, coordinates snapped to whole device pixels — rather than downscaled from a large master. A curved hairline hook shrunk to 16px dissolves into grey mush.

The hook keeps its inner cutout and open left side even at 16px: a solid block there is indistinguishable from a vertical bar. What carries legibility in the tray is the 1px gaps between the speaker, the leash bar and the hook, which keep them as three separate masses. The hook's corners are square at small sizes because rounded 1px walls break into diagonal-only fragments.

`IconAssetTests` enforces that. It decodes the shipped 16px frame and asserts it resolves into **exactly three bright masses** — the speaker, the leash bar and the hook — with at least 20 near-white pixels. Regenerating with `SIMPLIFIED_UP_TO = 0`, so every frame comes from the full hairline geometry, produces fragmented masses and few near-white pixels, and the tests fail. Do not relax those bounds to make a red test green; they mean the artwork regressed.

The tile geometry (corner radius 15.3% of width, vertical gradient, white glyph spanning ~72% of the width) is shared with the AudioStreamer icon so the two apps read as a family. AudioLeash's gradient is `#22C3DE` → `#0E7C96`.

The generator is deterministic: re-running it on an unchanged script reproduces the committed `.ico` byte-for-byte with the same Pillow version. That has only been verified within a single Pillow version — a Pillow upgrade could change its PNG encoder's output bytes without changing the rendered pixels.

## Building the Installer

Produces `installer\Output\AudioLeash-Setup.exe`, a per-user installer that needs no admin rights.

**Prerequisites (one-time):**
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)

**Run from the repo root (PowerShell):**

```powershell
.\build-installer.ps1
```

The installer checks for the [.NET 10 Windows Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), installs to `%LOCALAPPDATA%\AudioLeash` (no UAC prompt), adds a Start Menu entry, and offers an optional "Start with Windows" checkbox that pre-sets the same registry key the tray menu toggle manages.

### Versioning

The app version is defined once, as `<Version>` in `AudioLeash/AudioLeash.csproj`. The installer reads it back from the freshly published executable, so the setup version always matches the build. To stamp a different version at build time (e.g. from CI or a git tag) without editing the project file:

```powershell
.\build-installer.ps1 -Version 1.2.3
# from a git tag like "v1.2.3":
.\build-installer.ps1 -Version (git describe --tags --abbrev=0).TrimStart('v')
```

### Cutting a Release

1. Bump `<Version>` in `AudioLeash/AudioLeash.csproj`.
2. Add a section to [RELEASE_NOTES.md](RELEASE_NOTES.md).
3. Commit (`chore(release): X.Y.Z`) and push to `main`.
4. Tag and push: `git tag -a vX.Y.Z -m "AudioLeash X.Y.Z"` then `git push origin vX.Y.Z`.
5. Build the installer (`.\build-installer.ps1`) — the version is derived automatically.
6. Create the GitHub release for the tag and attach `AudioLeash-Setup.exe`.

## Dependencies

| Package | Purpose |
|---|---|
| `NAudio.Wasapi` 2.2.1 | Device enumeration and change events (`IMMNotificationClient`) |

## Project Structure

```
AudioLeash/
├── AudioLeash.sln
├── build-installer.ps1              ← Builds and packages the Inno Setup installer
├── installer/
│   └── AudioLeash.iss               ← Inno Setup script
├── tools/
│   └── generate-icon.py             ← Regenerates Resources/icon.ico (Pillow, dev-only)
├── AudioLeash/
│   ├── AudioLeash.csproj
│   ├── Program.cs                   ← Entry point; STA thread, WinForms bootstrap
│   ├── AudioLeashContext.cs         ← All application logic (tray, menu, device events)
│   ├── DeviceSelectionState.cs      ← Pure selection state machine (unit-testable)
│   ├── RankedDevice.cs              ← One entry in a ranked priority list (id + cached name)
│   ├── DeviceRebinder.cs            ← Pure logic: moves saved entries to an endpoint that replaced theirs (unit-testable)
│   ├── PriorityState.cs             ← Pure ranked-priority/override state machine (unit-testable)
│   ├── PriorityListEditor.cs        ← Pure list-editing logic behind the settings window
│   ├── PriorityListControl.cs       ← Owner-drawn ranked/unranked device list (settings window)
│   ├── SettingsForm.cs              ← Settings window (Playback / Recording tabs, Keep devices awake page, Start with Windows switch)
│   ├── KeepAliveState.cs            ← Pure keep-alive decision logic: which device should stream (unit-testable)
│   ├── KeepAliveRows.cs             ← Pure model behind the "Keep devices awake" page
│   ├── KeepAliveController.cs       ← Keeps at most one keep-alive stream matching the state (re-entrancy safe, unit-testable)
│   ├── KeepAliveStream.cs           ← Shared-mode WASAPI stream of silence on one device
│   ├── SilenceWaveProvider.cs       ← Endless zero-filled IWaveProvider (keep-alive "Zero" mode)
│   ├── KeepAlivePauseMonitor.cs     ← Hidden window reporting lock, display-off and suspend
│   ├── ToggleSwitch.cs              ← Windows 11-style on/off switch used by the settings window
│   ├── SettingsTheme.cs             ← Light/dark palette the settings window draws with
│   ├── UnderlineTabStrip.cs         ← Flat underline tabs used by the settings window
│   ├── ThemedControls.cs            ← Flat themed styling for buttons in the settings window
│   ├── PolicyConfigClient.cs        ← COM interop: sets Windows default audio endpoint
│   ├── SessionEndListener.cs        ← Exits on session end / installer Restart Manager close
│   ├── SettingsSnapshot.cs          ← Immutable snapshot of everything persisted (per-flow pick + ranked list, keep-alive)
│   ├── SettingsFile.cs              ← Pure JSON parse/serialize of the versioned file, incl. the v2.1.x upgrade
│   ├── SettingsStore.cs             ← Loads/saves the snapshot at %AppData%\AudioLeash\ (locked-file retry, .bak backup)
│   ├── StartupService.cs            ← Windows Run-key startup registration
│   ├── DarkMenuRenderer.cs          ← Dark mode context menu renderer
│   ├── WindowsTheme.cs              ← Windows theme detection (light/dark)
│   ├── TrayIconLoader.cs            ← Loads the tray icon at the requested frame size
│   └── Resources/
│       └── icon.ico                 ← tray icon
└── AudioLeash.Tests/
    ├── AudioLeash.Tests.csproj      ← xUnit + NSubstitute
    ├── ClearSelectionMessageTests.cs
    ├── DeviceRebinderTests.cs
    ├── DeviceSelectionStateTests.cs
    ├── IconAssetTests.cs
    ├── KeepAliveControllerTests.cs
    ├── KeepAlivePauseMonitorTests.cs
    ├── KeepAliveRowsTests.cs
    ├── KeepAliveStateTests.cs
    ├── PriorityListEditorTests.cs
    ├── PriorityStateTests.cs
    ├── SessionEndListenerTests.cs
    ├── SettingsFileTests.cs
    ├── SettingsStoreTests.cs
    ├── SilenceWaveProviderTests.cs
    ├── StartupServiceTests.cs
    ├── TrayIconLoaderTests.cs
    └── WindowsThemeTests.cs
```

## Architecture Notes

- Core logic lives in `AudioLeashContext.cs`. `DeviceSelectionState.cs` (pure, unit-testable selection state machine) and `PolicyConfigClient.cs` (COM interop that sets the Windows default endpoint) are intentional splits.
- `IMMNotificationClient` callbacks arrive on a Windows COM audio thread and are marshalled back to the UI/STA thread via `SafeInvoke` before touching any UI.
- `DeviceSelectionState.IsInternalChange` (volatile) prevents feedback loops where the app's own device switch would otherwise re-trigger the change handler.
- **Debounced device-state handling** — connect/disconnect events for the selected device don't act immediately. `OnDeviceStateChanged` (and the `Suspend` branch of `OnDefaultDeviceChanged`) re-arm a per-flow `System.Windows.Forms.Timer` (`DebounceMs`, 1000 ms). Only once the endpoint stops flapping does `ReconcileDeviceState` re-query the device's actual availability and call `DeviceSelectionState.EvaluateSettledState`, which returns a single `SettledOutcome` reflecting the net change. This collapses rapid flapping (e.g. an HDMI/eARC handshake) into at most one notification — a flap that ends back on the selected device restores it silently. The immediate `Restore` (leash-yank) path in `OnDefaultDeviceChanged` is unaffected and stays snappy.
- **Legacy vs. priority mode (per flow)** — a flow with an empty ranked list runs in legacy mode: `DeviceSelectionState` enforces the single pick exactly as before. A non-empty list switches the flow to priority mode, driven by `PriorityState`: the target is the highest-ranked connected device, or the manual override while one is in force. `PriorityState` guards its fields with a lock because the audio thread reads them in `EvaluateDefaultChange`; the debounced reconcile calls `EvaluateSettled`, whose `PriorityOutcome` drives one balloon at most. Ranked lists are saved with cached names, so absent devices still display.
- **Keep-alive** — `KeepAliveState` decides which device should stream: the Render/Multimedia default, if it has keep-alive enabled and nothing pauses it (lock, display off, suspend, reported by `KeepAlivePauseMonitor`'s hidden window). `KeepAliveController` stops and starts a single `KeepAliveStream` to match; starting or stopping blocks the UI thread in a CLR wait that still delivers sent power/session messages, so a re-entrant reconcile is folded into another pass instead of orphaning a stream. Default-device changes are coalesced by a 500 ms one-shot timer so restores and HDMI flaps don't churn the stream. `KeepAliveStream` uses NAudio's `WasapiOut` in shared mode with **polling** at 400 ms latency (about 5 wakeups a second; event sync would wake every ~10 ms engine period) and the device's mix format. All of its COM setup runs on a thread-pool (MTA) thread. A stream that stops unexpectedly reports a fault on its playback thread (the callback is wired before `Play()`, so even an immediate failure is caught); the context hands it to the controller with `BeginInvoke`, not `SafeInvoke`, because disposing the stream joins that thread, and retries on a 5 s one-shot timer. No stream, thread or timer exists while keep-alive isn't wanted. Enabled devices are saved in the top-level `keepAlive` list.
- **Re-binding replaced endpoints** — everything matches devices by endpoint ID, but a driver reinstall (e.g. an NVIDIA driver update reinstalling HDMI audio) can give the same device a new ID and drop the old one entirely. `DeviceRebinder` moves a ranked or keep-alive entry whose ID Windows no longer has in any state (`DeviceState.All`, NotPresent included) to an unclaimed Active/Unplugged/Disabled endpoint with the same name and description (case-insensitive): the single connected match, else the single match of any state; anything ambiguous is left alone. An ID that still exists is never touched — an unplugged USB device goes NotPresent, so an identical model can't take over its entry. It runs at startup before the flows load, and on a 1000 ms debounce after `OnDeviceAdded` or a device turning Active, then reconciles the affected flows. It is skipped while the settings window is open (closing it would save the window's stale IDs back) and re-checked when the window closes. The saved pick (the legacy locked device, or the manual override in priority mode) goes through the same rebinder; a pick upgraded from a v2.1.x file has no description and matches on name alone — Windows friendly names already embed the adapter name — and gains its description at the first startup where its device is present; at runtime a rebound legacy pick keeps its notified availability, so the reconcile reports the reconnect. A priority-mode override usually ends (`OverrideEnded`) when its old endpoint leaves, before the replacement arrives, so in practice it is only re-bound at startup.
- **Settings: live state is canonical** — while the app runs, the ranked lists and overrides (`PriorityState`), the legacy locked devices (`DeviceSelectionState`) and the keep-alive list (`KeepAliveState`) are the only copy of the settings; every pick is a `RankedDevice` (ID, name, description). `AudioLeashContext.BuildSnapshot` turns them into a `SettingsSnapshot`, and `SaveSettings()` hands it to `SettingsStore`, once after every change. Each flow saves one pick: the manual override in priority mode, the locked device in legacy mode (`FlowSnapshot.From`), so a restart restores it (an override under a short startup grace period). The file `%AppData%\AudioLeash\settings.json` is that snapshot, `"version": 2`, with `playback` and `recording` blocks (`{ pick, priority }`) and a top-level `keepAlive` list. A file with no `version` (v2.1.x) is upgraded on load by `SettingsFile` and written back in the new format at startup; a higher version counts as corrupt. `SettingsStore` skips a write whose JSON is unchanged, never throws, retries a locked read (4 attempts, 250 ms apart), and before overwriting a file it could not use copies it to `settings.json.bak` (if the copy fails, nothing is written and the copy is retried on the next save). An empty snapshot never overwrites an unusable file, so a bad file is left alone until the first real change. When the settings window closes because the app is exiting, its edits are applied to the live state without switching anything and saved; the only COM call is the best-effort read of the current default when a flow leaves priority mode.

## Known Issues

- None currently tracked.

## Roadmap

Ideas for future development, roughly in order of interest:

- **Hotkey cycling** — a global keyboard shortcut to cycle to the next audio device.
- **Communication device** — also set the "default communications device" alongside the default playback device.
- **Profiles** — named profiles that switch multiple devices (playback + recording) together. Could also address the boot-time race where a saved device hasn't finished initialising when the app starts: a profile-aware restore could defer until the target device comes online.
- **Per-app routing** — use Windows 10+ per-application audio settings where supported.
- **Volume indicator** — show or control master volume from the tray menu.
- **Fluctuate mode for keep-alive** — tiny non-zero samples, for hardware that ignores pure digital silence (as in Sound Keeper's default mode).

### Already shipped

Windows-startup toggle · recording-device support · settings persistence · tray tooltip · dark-mode menu · single-instance enforcement · persistent selection for unavailable devices · device priority · keep devices awake.
