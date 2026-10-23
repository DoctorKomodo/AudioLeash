# AudioLeash

**Keeps Windows on a leash** — a lightweight system tray app that stops Windows from switching your audio device behind your back, and snaps it right back when it tries.

Plug in a USB headset, dock your laptop, or join a call, and Windows loves to silently hijack your default speaker or microphone. AudioLeash watches for that and instantly restores the device *you* chose.

---

## Install

1. Download the latest **`AudioLeash-Setup.exe`** from the [**Releases page**](https://github.com/DoctorKomodo/AudioLeash/releases/latest).
2. Run it. The installer is per-user — **no administrator rights and no UAC prompt** required.
3. AudioLeash appears in your system tray (the notification area next to the clock). That's all there is to it.

During setup you can tick **"Start with Windows"** so AudioLeash is on guard after every reboot. You can change this any time from the tray menu.

> **Requirements:** Windows 10 or 11, and the **.NET 10 Desktop Runtime**. If it isn't installed, AudioLeash offers you the download link the first time you launch it.

---

## Using AudioLeash

AudioLeash lives in the system tray. **Click the tray icon** (left- or right-click) to open the menu.

- **Lock your speaker** — pick a device under the **Playback** section. AudioLeash sets it as the Windows default and keeps it there. A ✔ marks your choice.
- **Lock your microphone** — pick a device under the **Recording** section. It's locked independently of playback.
- **Spot a hijack** — if Windows has quietly switched away from your pick, the current default is labelled **(Windows default)** so you always know the real state.
- **Clear Selection** — stop locking and let Windows manage devices on its own again. (With a priority list set up, **Return to priority order** takes its place — see below.)
- **Settings…** — open the settings window to rank your devices (see [Device priority](#device-priority)) or keep them awake (see [Keep devices awake](#keep-devices-awake)).
- **Start with Windows** — toggle launch-at-login on or off. Also available as a switch at the bottom of the settings window.
- **Exit** — quit the app.

Once a device is locked you can forget about it. If Windows switches away — because you plugged something in, or an app grabbed the default — AudioLeash switches back automatically and shows a brief notification.

### When a device disconnects

Unplug your locked headset and AudioLeash doesn't forget it. The device stays selected (shown greyed-out as **(unavailable)**, with **(waiting)** in the tray tooltip). The moment you plug it back in, AudioLeash restores it as the default. Your choice survives reboots, too — if the device isn't connected at startup, AudioLeash simply waits for it.

Some devices flicker off and on rapidly during normal use — an HDMI/eARC TV that briefly drops audio while a receiver wakes up, for example. AudioLeash waits for the device to settle before reacting, so a burst of disconnects and reconnects produces a single notification at most (and none at all if the device ends up right back where it started).

### Device priority

Instead of locking one device, you can rank several — say headphones first, speakers second, the TV third. Choose **Settings…** in the tray menu; the **Playback** and **Recording** tabs each have one list:

- **Ranked devices** sit at the top, numbered in priority order. Drag a device to reorder it, or select it and use **▲**/**▼**.
- Everything below the line *"Not ranked: AudioLeash won't switch to these on its own"* is left alone. Drag a device across the line (or use **▲**/**▼**) to rank or unrank it.
- A ranked device that's unplugged keeps its place and shows **not connected**.
- **Driver updates** — updating a driver (a GPU driver update reinstalls HDMI audio, for example) can make Windows treat a device as brand new. AudioLeash spots the replacement by its name and moves your ranking and keep-awake setting over to it automatically, at startup or as soon as the device appears.
- There's no Save button: your new order takes effect when you close the settings window, so rearranging never switches your audio halfway. To undo a change, move the device back before closing.

With a list in place, AudioLeash keeps the highest-ranked **connected** device as the default. Plug in a higher-ranked device and it switches up to it; unplug the current one and it falls back to the next. It never switches on its own to a device you haven't ranked. In the tray menu, ranked devices show their rank (e.g. `Headphones  #1`).

- **Manual picks** — picking any device in the tray menu, ranked or not, holds it there regardless of the order (its line says **(manual)**). Plugging other devices in or out doesn't knock you off it, and it survives a restart. It ends when that device disconnects, and AudioLeash goes back to the priority order.
- **Return to priority order** — appears in the tray menu while a manual pick is in force; it drops the pick and switches straight to your top connected ranked device.
- **No list, no change** — if you never rank anything, AudioLeash works exactly as described above. Clearing a list and saving makes whatever device is currently the default your locked device again.

### Keep devices awake

Some receivers and TVs drop their HDMI or S/PDIF audio link after a few seconds of quiet, so the start of the next sound gets cut off. Dolby Atmos from a PC to an AV receiver is a common example. AudioLeash can keep the link awake by playing pure digital silence to the device.

- Open **Settings…**, choose **Keep devices awake**, and switch it on for each device that needs it. Changes apply immediately.
- It runs only while that device is the Windows default playback device, and follows the default as it changes.
- The silence is inaudible, and other apps' sound plays through it normally. While it's running, AudioLeash appears in the Windows volume mixer.
- It pauses while your PC is locked, the screen is off, or the PC is asleep, and resumes afterwards.
- **Sleep:** while it's running, Windows counts audio as playing, so the PC won't go to sleep on its own while it's unlocked and the screen is on. Set the screen to turn off before the PC sleeps, or lock the PC when you walk away.
- Some hardware ignores pure silence and still drops the link. A mode that adds tiny, inaudible signal may come later.

---

## Features at a glance

- **Locks your audio devices** — separate locks for playback and microphone, set independently.
- **Auto-restore** — instantly reverts Windows' unwanted device switches.
- **Survives unplugging** — keeps your choice and reapplies it the moment the device returns.
- **Device priority** — rank your devices and AudioLeash always uses the best one that's connected, switching up and falling back automatically.
- **Manual override** — pick any device from the tray to hold it until you return to priority order or it disconnects.
- **Keep devices awake** — plays inaudible silence to chosen devices while they're the default, so HDMI receivers and TVs don't cut off the start of sounds.
- **Remembers across reboots** — your selection, priority lists and manual picks are saved and restored at startup.
- **Dark mode** — the tray menu and settings window follow your Windows light/dark theme.
- **Featherweight** — lives in the tray (plus a small settings window), and only ever one instance running.

---

## Uninstall

Uninstall from **Settings → Apps → Installed apps → AudioLeash**, or use the **Uninstall AudioLeash** shortcut in the Start Menu. Your preferences are stored separately in `%AppData%\AudioLeash\` and can be deleted by hand if you want them gone too.

---

## Building from source & contributing

Developer documentation — building, running tests, packaging the installer, project layout, architecture notes, known issues, and the roadmap — lives in **[DEVELOPMENT.md](DEVELOPMENT.md)**.

## A note on how this was built

AudioLeash was developed with the help of AI tools. Some people feel strongly about that, one way or the other — this note is here so you can make an informed choice. The code is open for anyone to read, review, and audit.

## License

Released under the [MIT License](LICENSE).
