# AudioLeash — Release Notes

## v1.2.0

This release stops notification spam from flapping devices, and replaces the tray icon with one you can actually recognise.

### Improvements

**No more notification bursts when a device flaps**
- A device that rapidly disconnects and reconnects — an HDMI/eARC TV black-screening while a receiver wakes, for example — used to fire one balloon per transition, producing five or more "Disconnected" notifications followed by five or more "Reconnected" ones.
- AudioLeash now waits for the device to settle before reacting, and shows **at most one notification** reflecting the net change. A flap that ends back on your selected device restores it silently, with no notification at all.
- Yanking the leash — restoring your device when Windows switches away from it — is unaffected and stays immediate.

**A tray icon you can read at tray size**
- The icon is now a cyan tile carrying a white speaker and carabiner, replacing the thin waveform that dissolved into a pale smear at 16x16.
- The small sizes are drawn specifically for the notification area rather than shrunk down from a large image, so the icon stays sharp on both light and dark taskbars.

### Fixes

- The tray icon was being loaded at 32x32 and scaled down to 16x16 by Windows, which blurred it. AudioLeash now loads the correctly-sized image.

### Requirements

- Windows 10 or 11
- .NET 10 Windows Desktop Runtime — if it is missing, AudioLeash offers you the download link the first time you launch it

> **Note:** the installer no longer checks for the .NET runtime itself. That check consulted the 32-bit view of the registry and so false-positived on machines that already had the 64-bit desktop runtime installed.

---

## v1.1.0

This release adds recording-device support, smarter handling of devices that come and go, and a number of tray-experience improvements.

### New Features

**Recording / Microphone Device Support**
- The tray menu now has two sections — **Playback** and **Recording** — each with its own header.
- You can lock a playback device and a recording device independently; AudioLeash restores whichever one Windows changes.
- "Clear Selection" resets both at once. Settings from older versions are migrated automatically.

**Persistent Selection for Unavailable Devices**
- When your selected device is unplugged or disabled, AudioLeash now **keeps the selection and suspends enforcement** instead of clearing it (the previous behaviour).
- The device appears in the tray menu as a grayed-out, checked **(unavailable)** entry, and the tooltip shows **(waiting)**.
- When the device reconnects, it is **restored automatically** with a "Device Reconnected" notification — including devices that were disconnected at startup.

**Dark Mode Menu**
- The tray context menu adapts to the Windows colour theme: a dark background with light text in dark mode, standard appearance in light mode.
- Switches live — changing the Windows theme is reflected the next time the menu opens, no restart needed.

**Tray Tooltip Shows Selected Device**
- Hovering the tray icon shows the locked playback and recording device names (with a "(waiting)" suffix when a device is disconnected).

### Improvements

- The device list now **auto-refreshes every time the tray menu opens**, so it always reflects the current set of devices.
- Device **friendly names are persisted** alongside their IDs, so the correct name is shown even when the device is disconnected at startup.

### Build & Packaging

- The installer version is now **derived from the built executable**, with `AudioLeash.csproj` `<Version>` as the single source of truth. `build-installer.ps1` accepts an optional `-Version` override for CI/tag-driven builds.

### Requirements

- Windows 10 or 11
- .NET 10 Windows Desktop Runtime (the installer checks for it and warns if absent)

---

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
