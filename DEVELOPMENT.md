# AudioLeash — Development

Developer-facing documentation: building, testing, packaging, architecture, and the roadmap. For install and usage, see [README.md](README.md).

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

The script renders six frames (16, 32, 48, 64, 128, 256). The 16px and 32px frames are drawn from **simplified geometry** — a thicker carabiner, no gate detail, coordinates snapped to whole device pixels — rather than downscaled from a large master. A hairline ring shrunk to 16px dissolves into grey mush, which is exactly what the previous icon did.

The ring keeps its inner cutout even at 16px: a solid pill at that size is indistinguishable from a vertical bar. What carries legibility in the tray is the 1px gap between the speaker and the ring, which keeps them as two separate masses.

`IconAssetTests` enforces that. It decodes the shipped 16px frame and asserts it resolves into **exactly two bright masses** — the speaker and the ring — with at least 20 near-white pixels. Regenerating with `SIMPLIFIED_UP_TO = 0`, so every frame comes from the full hairline geometry, produces five fragmented masses and eight near-white pixels, and the tests fail. Do not relax those bounds to make a red test green; they mean the artwork regressed.

The tile geometry (corner radius 15.3% of width, vertical gradient, white glyph at ~54% width) is shared with the AudioStreamer icon so the two apps read as a family. AudioLeash's gradient is `#22C3DE` → `#0E7C96`.

The generator is deterministic: re-running it on an unchanged script reproduces the committed `.ico` byte-for-byte.

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
├── AudioLeash/
│   ├── AudioLeash.csproj
│   ├── Program.cs                   ← Entry point; STA thread, WinForms bootstrap
│   ├── AudioLeashContext.cs         ← All application logic (tray, menu, device events)
│   ├── DeviceSelectionState.cs      ← Pure selection state machine (unit-testable)
│   ├── PolicyConfigClient.cs        ← COM interop: sets Windows default audio endpoint
│   ├── SettingsService.cs           ← JSON settings persistence (%AppData%\AudioLeash\)
│   ├── StartupService.cs            ← Windows Run-key startup registration
│   ├── DarkMenuRenderer.cs          ← Dark mode context menu renderer
│   ├── WindowsTheme.cs              ← Windows theme detection (light/dark)
│   └── Resources/
│       └── icon.ico                 ← tray icon
└── AudioLeash.Tests/
    ├── AudioLeash.Tests.csproj      ← xUnit + NSubstitute
    ├── DeviceSelectionStateTests.cs
    ├── SettingsServiceTests.cs
    ├── StartupServiceTests.cs
    └── WindowsThemeTests.cs
```

## Architecture Notes

- Core logic lives in `AudioLeashContext.cs`. `DeviceSelectionState.cs` (pure, unit-testable selection state machine) and `PolicyConfigClient.cs` (COM interop that sets the Windows default endpoint) are intentional splits.
- `IMMNotificationClient` callbacks arrive on a Windows COM audio thread and are marshalled back to the UI/STA thread via `SafeInvoke` before touching any UI.
- `DeviceSelectionState.IsInternalChange` (volatile) prevents feedback loops where the app's own device switch would otherwise re-trigger the change handler.
- **Debounced device-state handling** — connect/disconnect events for the selected device don't act immediately. `OnDeviceStateChanged` (and the `Suspend` branch of `OnDefaultDeviceChanged`) re-arm a per-flow `System.Windows.Forms.Timer` (`DebounceMs`, 1000 ms). Only once the endpoint stops flapping does `ReconcileDeviceState` re-query the device's actual availability and call `DeviceSelectionState.EvaluateSettledState`, which returns a single `SettledOutcome` reflecting the net change. This collapses rapid flapping (e.g. an HDMI/eARC handshake) into at most one notification — a flap that ends back on the selected device restores it silently. The immediate `Restore` (leash-yank) path in `OnDefaultDeviceChanged` is unaffected and stays snappy.
- The user-selected device (ID and friendly name) is persisted to `%AppData%\AudioLeash\settings.json` via `SettingsService` and restored on startup.

## Known Issues

- **`SelectedDeviceId` race condition** — `OnDefaultDeviceChanged` reads `SelectedDeviceId` twice on the audio thread (once inside `EvaluateDefaultChange`, once to capture `restoreId`) with no lock between them. If the user clicks "Clear Selection" on the UI thread in that gap, `restoreId` will be `null` despite the `!` assertion. Extremely unlikely in practice, and the inner `try`/`catch` handles the resulting exception gracefully, but a proper fix would snapshot `SelectedDeviceId` once under a lock and pass the snapshot through.

## Roadmap

Ideas for future development, roughly in order of interest:

- **Hotkey cycling** — a global keyboard shortcut to cycle to the next audio device.
- **Communication device** — also set the "default communications device" alongside the default playback device.
- **Profiles** — named profiles that switch multiple devices (playback + recording) together. Could also address the boot-time race where a saved device hasn't finished initialising when the app starts: a profile-aware restore could defer until the target device comes online.
- **Per-app routing** — use Windows 10+ per-application audio settings where supported.
- **Volume indicator** — show or control master volume from the tray menu.

### Already shipped

Windows-startup toggle · recording-device support · settings persistence · tray tooltip · dark-mode menu · single-instance enforcement · persistent selection for unavailable devices.
