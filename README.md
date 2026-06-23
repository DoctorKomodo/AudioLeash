# AudioLeash

**Keeps Windows on a leash** — a lightweight system tray app that stops Windows from switching your audio device behind your back, and snaps it right back when it tries.

Plug in a USB headset, dock your laptop, or join a call, and Windows loves to silently hijack your default speaker or microphone. AudioLeash watches for that and instantly restores the device *you* chose.

---

## Install

1. Download the latest **`AudioLeash-Setup.exe`** from the [**Releases page**](https://github.com/DoctorKomodo/AudioLeash/releases/latest).
2. Run it. The installer is per-user — **no administrator rights and no UAC prompt** required.
3. AudioLeash appears in your system tray (the notification area next to the clock). That's all there is to it.

During setup you can tick **"Start with Windows"** so AudioLeash is on guard after every reboot. You can change this any time from the tray menu.

> **Requirements:** Windows 10 or 11, and the **.NET 10 Desktop Runtime**. If it isn't installed, the setup wizard points you to the download.

---

## Using AudioLeash

AudioLeash has no window — it lives entirely in the system tray. **Click the tray icon** (left- or right-click) to open the menu.

- **Lock your speaker** — pick a device under the **Playback** section. AudioLeash sets it as the Windows default and keeps it there. A ✔ marks your choice.
- **Lock your microphone** — pick a device under the **Recording** section. It's locked independently of playback.
- **Spot a hijack** — if Windows has quietly switched away from your pick, the current default is labelled **(Windows default)** so you always know the real state.
- **Clear Selection** — stop locking and let Windows manage devices on its own again.
- **Start with Windows** — toggle launch-at-login on or off.
- **Exit** — quit the app.

Once a device is locked you can forget about it. If Windows switches away — because you plugged something in, or an app grabbed the default — AudioLeash switches back automatically and shows a brief notification.

### When a device disconnects

Unplug your locked headset and AudioLeash doesn't forget it. The device stays selected (shown greyed-out as **(unavailable)**, with **(waiting)** in the tray tooltip). The moment you plug it back in, AudioLeash restores it as the default. Your choice survives reboots, too — if the device isn't connected at startup, AudioLeash simply waits for it.

---

## Features at a glance

- **Locks your audio devices** — separate locks for playback and microphone, set independently.
- **Auto-restore** — instantly reverts Windows' unwanted device switches.
- **Survives unplugging** — keeps your choice and reapplies it the moment the device returns.
- **Remembers across reboots** — your selection is saved and restored at startup.
- **Dark mode menu** — the tray menu matches your Windows light/dark theme, updating live.
- **Featherweight** — tray-only, no window, and only ever one instance running.

---

## Uninstall

Uninstall from **Settings → Apps → Installed apps → AudioLeash**, or use the **Uninstall AudioLeash** shortcut in the Start Menu. Your preferences are stored separately in `%AppData%\AudioLeash\` and can be deleted by hand if you want them gone too.

---

## Building from source & contributing

Developer documentation — building, running tests, packaging the installer, project layout, architecture notes, known issues, and the roadmap — lives in **[DEVELOPMENT.md](DEVELOPMENT.md)**.

## License

Released under the [MIT License](LICENSE).
