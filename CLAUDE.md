# CRITICAL

- NEVER leave uncommitted or unpushed changes — always maintain a consistent and backed-up repository state
- ALWAYS update README.md when changes affect user-facing behaviour, features, setup steps, or project structure
- ALWAYS consider web research before non-trivial implementations — patterns, existing NuGet packages, API quirks (especially Windows Core Audio edge cases)
- Use context7 MCP to look up external library docs (especially NAudio) before implementing — do not guess API shapes

---

# PROJECT OVERVIEW

**AudioLeash** is a lightweight Windows system tray application written in **C# (.NET 10)** that prevents Windows from automatically switching the user's audio output/input device. It detects external changes and restores the user's chosen device.

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# 14 (.NET 10) |
| UI | Windows Forms (WinForms) — system tray only |
| Audio API | NAudio.Wasapi 2.2.1 (`MMDeviceEnumerator`, `IMMNotificationClient`) |
| Default device | `PolicyConfigClient.cs` (self-contained COM interop) |
| Target OS | Windows 10 / 11 only |

## Project Structure

```
AudioLeash/
├── AudioLeash.sln
├── build-installer.ps1              ← Builds and packages the Inno Setup installer
├── CLAUDE.md                        ← this file
├── README.md
├── RELEASE_NOTES.md
├── installer/
│   └── AudioLeash.iss               ← Inno Setup script
├── AudioLeash/
│   ├── AudioLeash.csproj
│   ├── AssemblyInfo.cs
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

## Key Design Constraints

- Core logic lives in `AudioLeashContext.cs`. `DeviceSelectionState.cs` and `PolicyConfigClient.cs` are intentional splits: the former enables unit testing of pure logic; the latter isolates COM interop.
- `DeviceSelectionState.IsInternalChange` (volatile) prevents feedback loops when the app itself triggers a device change.
- The user-selected device is persisted to `%AppData%\AudioLeash\settings.json` via `SettingsService` and restored on startup.
- **Known race condition**: `OnDefaultDeviceChanged` reads `SelectedDeviceId` twice on the audio thread without a lock. If the user clicks "Clear Selection" between reads, `restoreId` could be null despite the `!` assertion. The inner `try/catch` handles this gracefully, but a proper fix would snapshot under a lock.

---

# WORKFLOW

## Code Review

After completing a task, review your changes with a subagent that evaluates correctness, big-picture implications, and code quality. Address findings before presenting to the user.

## Future Development

Ideas for future development are tracked in README.md. Do not implement features from that list unless explicitly requested.

## Testing

Use **xUnit** for unit tests and **NSubstitute** for mocking. Tests live in `AudioLeash.Tests/`.

### What to Test

- **Unit test** pure logic: device selection state, loop-prevention flag transitions, unavailable-device handling
- **Do not** write tests requiring a real Windows audio stack — mock or extract pure logic
- **Do not** test WinForms UI mechanics directly — extract logic into testable classes first

### Quality Bar

- Every new feature must include corresponding tests before merge
- All tests must pass (`dotnet test`) before requesting a merge review

---

# BUILD & DEVELOPMENT COMMANDS

```bash
dotnet build AudioLeash.sln -c Release    # Build
dotnet test AudioLeash.sln                 # Run tests
```

> The application can only be **run** on Windows. Build and test steps may execute in a Linux CI environment (excluding tests requiring the Windows audio stack).
