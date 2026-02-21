# AudioLeash — Release Notes

## v1.0.0 — Initial Release

AudioLeash is a lightweight Windows system tray application that prevents Windows from automatically switching your audio output device.

### Features

**System Tray Interface**
- Runs headlessly — no main window, lives entirely in the notification area
- Left- or right-click the tray icon to open the device menu
- Single-instance enforced; a second launch exits silently

**Audio Device Control**
- Lists all active playback devices, sorted alphabetically, refreshed on every menu open
- Selecting a device sets it as the Windows default immediately and marks it with a checkmark (✔)
- If Windows has switched to a different device independently, that device is labelled "(Windows default)" so you always know the current state

**Auto-Restore (Anti-Hijack)**
- Monitors Windows audio device change events in real time
- When an external event (e.g. plugging in USB headphones) causes Windows to switch devices, AudioLeash detects and reverts to your chosen device automatically
- A balloon tip confirms every automatic restore

**Graceful Degradation**
- If your selected device is unplugged or disabled, the selection is cleared rather than causing repeated restore failures, and you are notified via balloon tip

**Persistence & Startup**
- Selected device is saved to `%AppData%\AudioLeash\settings.json` and restored on every launch
- If the saved device is no longer available at startup, you are prompted to choose again
- "Start with Windows" toggle in the tray menu registers AudioLeash in the Windows Run registry key so it launches automatically at login

**Installer**
- Single-file `AudioLeash-Setup.exe` built with Inno Setup
- Installs to `%LOCALAPPDATA%\AudioLeash` — no UAC prompt required
- Checks for .NET 8 Windows Desktop Runtime and warns if absent
- Adds a Start Menu entry and an optional "Start with Windows" checkbox (enabled by default)

### Requirements

- Windows 10 or 11
- .NET 8 Windows Desktop Runtime (bundled check in installer)

### Known Issues

- **`SelectedDeviceId` race condition** — if "Clear Selection" is clicked at the exact moment an auto-restore is in progress, the restore may catch an unexpected null and log an error. The inner exception handler recovers cleanly; no data loss occurs.
