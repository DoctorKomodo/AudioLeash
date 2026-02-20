# AudioLeash

Keeps Windows on a leash — a lightweight system tray app that stops Windows from switching your audio output without permission, and snaps it back when it tries.




---

## Quick Start

### Requirements
- Windows 10 or 11
- .NET 8 SDK (to build) or .NET 8 Runtime (to run)

### Build
```
dotnet build AudioLeash.sln -c Release
```

### Run
```
AudioLeash\bin\Release\net8.0-windows\AudioLeash.exe
```

No window appears — the app lives entirely in the system notification area (tray).

### NuGet Dependencies
| Package | Purpose |
|---|---|
| `AudioSwitcher.AudioApi.CoreAudio` 3.1.0 | Device enumeration, default-device switching, change events |

---

## Current Feature Set

### 1. System Tray Icon
- The application runs headlessly — no main window is shown.
- An icon appears in the Windows notification area.
- **Left-click** or **right-click** the icon to open the device menu.
- The icon falls back to the standard system application icon if no custom `Resources/icon.ico` is found.

### 2. Audio Device Listing
- On every menu open, the current list of **active (enabled) playback devices** is fetched from the Windows Core Audio API.
- Devices are sorted alphabetically by their full display name.
- If no active devices are found, a disabled "No devices available" item is shown.

### 3. Default Device Selection
- Clicking a device in the menu:
  - Sets it as the Windows default playback device immediately.
  - Stores its ID as the **user-selected device** for auto-restore purposes.
  - Shows a balloon tip confirming the change.
  - Refreshes the menu (checkmark moves to the newly selected device).

### 4. Visual State Indicators
- A **checkmark (✔)** is shown next to the device the user has explicitly selected.
- If Windows has a different device set as default than the user's selection (e.g. after a plug event), that device is labelled with **"(Windows default)"** for clarity.

### 5. Automatic Device Restore (Anti-Hijack)
- The app subscribes to the Windows `AudioDeviceChanged` event stream.
- If an **external change** sets a different device as the default (e.g. plugging in a USB headset causes Windows to auto-switch), the app detects this and **automatically switches back** to the user-selected device.
- The restore is accompanied by a balloon tip notification.

### 6. Graceful Handling of Unavailable Devices
- If the user-selected device becomes unavailable (unplugged, disabled), the app **clears the selection** rather than attempting to switch to a missing device.
- A warning balloon tip informs the user.

### 7. Internal Change Flag (Loop Prevention)
- An `isInternalChange` boolean guards against feedback loops where the app's own device switch triggers the change-monitoring handler, which would then try to switch again.

### 8. Clear Selection / Disable Auto-Restore
- The **"Clear Selection"** menu item removes the stored device preference.
- After clearing, the app will no longer attempt to restore any device when Windows changes the default.
- The item is greyed out when no device is selected.

### 9. Manual Refresh
- A **"Refresh List"** menu item re-queries and rebuilds the device list on demand, useful if a device was connected/disconnected while the menu was already open.

### 10. Thread Safety
- The `AudioDeviceChanged` event arrives on a background Rx/IO thread.
- All UI updates (balloon tips, menu refresh) are marshalled back to the UI thread via `Control.InvokeRequired` / `Control.Invoke`.

### 11. Custom Icon Support
- Place a file named `icon.ico` in a `Resources/` subfolder next to the executable to use a custom tray icon.
- The app falls back to the Windows default application icon automatically if the file is missing.

### 12. Clean Exit
- The **"Exit"** menu item hides the tray icon and terminates the application.
- All resources (`NotifyIcon`, `ContextMenuStrip`, `CoreAudioController`) are properly disposed.

---

## Project Structure

```
AudioLeash/
├── AudioLeash.sln
└── AudioLeash/
    ├── AudioLeash.csproj
    ├── Program.cs               ← Entry point; runs AudioSwitcherContext
    ├── AudioSwitcherContext.cs  ← All application logic
    └── Resources/
        └── icon.ico             ← (optional) custom tray icon
```

---

## Ideas for Future Development

- **Windows startup** — Add/remove a registry `Run` key to launch the app at login.
- **Hotkey cycling** — Global keyboard shortcut to cycle to the next audio device.
- **Communication device** — Also set the "default communications device" alongside the default playback device.
- **Recording device support** — Extend to microphone/input devices.
- **Profiles** — Named profiles that switch multiple devices (playback + recording) together.
- **Per-app routing** — Use Windows 10+ per-application audio settings where supported.
- **Settings persistence** — Save selected device across restarts (JSON or registry).
- **Tooltip on hover** — Show the currently selected device name in the tray icon tooltip.
- **Dark/light theme icon** — Switch icon variant based on Windows theme.
- **Volume indicator** — Show or control master volume from the tray menu.
