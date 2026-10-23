# Keep Alive: Design

**Date:** 2026-10-20
**Status:** Approved

## Intent

The user plays Dolby Atmos from the PC over HDMI to an AV receiver. After a few seconds of quiet, the receiver drops the audio link, so the start of the next sound is cut off. [Sound Keeper](https://github.com/vrubleg/soundkeeper) fixes this for them by playing a constant inaudible stream to the output. The user wants the same fix inside AudioLeash, as an option they can turn on for specific playback devices.

This first version takes only Sound Keeper's **Zero** mode, which plays digital silence. It also borrows the core of Sound Keeper's **Sleepy** switch: the stream pauses when the PC is locked or the display is off, so the feature doesn't keep the machine awake.

Sound Keeper warns that Zero "may be not enough for some hardware". Its default mode, **Fluctuate**, plays silence with occasional tiny non-zero samples. The user knows about this and wants to try Zero first. The design leaves room to add Fluctuate later without reworking anything.

## Goals

1. A per-device **keep alive** setting for **playback** devices.
2. While enabled, AudioLeash plays digital silence (Zero mode) to that device **only while it is the current Windows default playback device**.
3. Keep-alive **pauses while the session is locked or the display is off**, and resumes when both clear.
4. **Lightweight:** negligible CPU and memory while running, and nothing running at all while not needed. It runs all day, so this is a stated requirement from the user.
5. Managed from a new **"Keep devices awake" page** in the settings window, reached from a settings card.

## Non-goals

- No other Sound Keeper modes (Fluctuate, Sine, noise) and no amplitude, frequency or timing parameters. The code leaves a seam for a Fluctuate mode later, but none of it ships in the UI now.
- No keep-alive for recording devices.
- No keep-alive for a device that isn't the current default. The user's decision: it follows the active device.
- No device-group selection (Sound Keeper's "All", "Digital" and so on). It's per device only.
- No tray menu items for keep-alive in this version.
- Nothing from DEVELOPMENT.md's roadmap.

## Anti-goals (it fails if…)

- …it is audible in any way.
- …it measurably costs CPU, wakeups or memory growth while idle. It should be cheap enough that the user forgets it's running.
- …it stops Windows from sleeping or turning the display off once the user has locked the PC or the display has turned off. (Idle sleep while unlocked with the display on is a known, accepted limitation. See Pause conditions.)
- …it gets in the way of AudioLeash's main job: switching, restoring, or the `IsInternalChange` loop guard.
- …a missing, unplugged or busy device produces error dialogs or balloons. Keep-alive is always best-effort and silent about failures.
- …a failed or interrupted stream leaves it dead until restart. It must recover by itself once conditions allow.

## Behaviour

### When the stream runs

Keep-alive streams to device **D** exactly when all of these are true:

- D is the current Windows default **playback** endpoint for the **Multimedia** role. This is the same role AudioLeash enforces today.
- D has keep-alive enabled.
- D is connected (`DeviceState.Active`).
- The session is not locked.
- The display is not off.
- The system is not suspending or suspended.

At most one stream exists at any time. When any input changes, AudioLeash works out the desired device (D or none) and reconciles:

- desired == current: do nothing.
- otherwise: stop the current stream (if any), then start one on the desired device (if any).

**Default changes.** When AudioLeash switches the default itself, or anything else does, the stream follows. It moves to the new default if that device has keep-alive on, and otherwise stops. A short coalescing delay (builder's call, around 500 ms) lets brief flaps settle, such as an AudioLeash restore or an HDMI handshake, so it doesn't churn the stream. Brief gaps in the stream during a switch are fine.

### Stream content and format (Zero mode)

- A WASAPI **shared-mode** render stream on D, in the device's mix format, filled with zeroes.
- Shared mode means every other app's audio mixes in normally. Don't mute the session or set its volume to zero. The samples are already zero, so muting gains nothing, and a muted session could be treated differently by the engine or drivers.
- Expect AudioLeash to show up in the Windows volume mixer while streaming. That's acceptable.

### Recovery

The stream can stop for reasons outside our control: an exclusive-mode app takes the device, the audio service restarts, the format changes, or the device is invalidated. When that happens, keep-alive stays wanted but not running, and retries quietly on a slow timer (builder's call, a few seconds, optionally backing off) until it either succeeds or the desired device changes. Sound Keeper behaves the same way and waits out exclusive-mode use. Failures are never shown to the user. Debug logging is fine.

### Pause conditions

- **Lock / unlock:** session switch events (`SystemEvents.SessionSwitch`: `SessionLock` / `SessionUnlock`, or `WTSRegisterSessionNotification`). Builder's call.
- **Display off / on:** `RegisterPowerSettingNotification` with `GUID_CONSOLE_DISPLAY_STATE` (0 = off, 1 = on, 2 = dimmed). **Dimmed counts as on.** This needs a window handle. A hidden window like `SessionEndListener` works, and extending that class or adding a sibling is the builder's call.
- **Suspend / resume:** stop on suspend, then re-evaluate on resume (`SystemEvents.PowerModeChanged`). Resume also re-checks the default device, which may have changed while the machine slept.
- On startup, assume unlocked and display on unless the APIs report otherwise.
- **Known limitation, accepted by the user:** an active render stream counts as audio playing, so it blocks Windows' automatic idle sleep. If the PC is left unlocked and the display-off timeout is longer than the sleep timeout (or the display never turns off), keep-alive keeps the PC awake. There is deliberately **no idle-input timer** to cover this: the user doesn't want a timer, and one could pause keep-alive during a long movie. Document this in the README, with the fix: set the display to turn off before the PC sleeps, or lock the PC.

### Settings page (UI)

The user chose **option 2** from the mockups: a settings card that opens a sub-page. The existing Playback and Recording tabs stay purely about device priority.

- **Card on the main page:** "Keep devices awake", with a subtitle that summarises the state (e.g. "Off" / "On for 1 device" / "On for 2 devices") and a `›` chevron. It sits in the card area next to "Start with Windows" and uses the same card styling. Clicking anywhere on it (or pressing Enter / Space when it has focus) opens the sub-page.
- **The sub-page** replaces the window content. It's not a new window. It shows:
  - a back link / header "‹ Settings › Keep devices awake" (wording is the builder's call), which returns to the main page.
  - a short explanation, e.g. "Plays inaudible silence to these devices while they're the default, so receivers and TVs don't cut off the start of sounds. Pauses while your PC is locked or the screen is off."
  - **every playback device Windows knows about** (active, unplugged or disabled), plus enabled devices that Windows no longer lists (from the saved names), each with a `ToggleSwitch`. Disconnected devices show "not connected" in the muted text style, as in the priority list. Order: by name. *(Design call.)*
- **Switches apply immediately**, like Start with Windows. They don't wait for the window to close, because they never change the default device. *(Design call.)* Toggling the current default on starts the stream straight away, and toggling it off stops it.
- **Esc** on the sub-page goes back to the main page. On the main page, it still closes the window. *(Design call.)* Ctrl+Tab and similar tab shortcuts do nothing on the sub-page.
- It follows `SettingsTheme` for light and dark, has accessible names on the switches and the card, and scales with DPI like the rest of the form.
- The main page's priority edits still apply on close. Opening the sub-page and coming back must not lose or apply them early.

### Persistence

- A new field in `settings.json`: `KeepAliveDevices`, a list of `{ id, name, description? }` that reuses the `RankedDevice` record shape, sanitised the same way (drop empty or duplicate IDs). Names let the page show enabled devices that are currently missing.
- The `SettingsService` API is the builder's call: load/save the list, preserving all other fields, as the existing `Save*` methods do.
- A missing field means nothing is enabled, so existing users see no change.

## Architecture

The split follows the existing pattern: pure logic in a testable class, side effects in thin wrappers, wiring in `AudioLeashContext`.

- **`KeepAliveState`** (pure, unit-tested): holds the enabled set and the current inputs (default device ID, whether it's active, locked, display off, suspended). It exposes the desired device ID (or null) and/or a reconcile decision (Start(id) / Stop / StartAfterStop / None) given what's running now. All the "when the stream runs" rules live here.
- **`KeepAliveStream`** (thin, not unit-tested): owns a single shared-mode render stream on one `MMDevice`. It has Start/Stop/Dispose and raises an event when the stream stops unexpectedly. The silence source is a separate `IWaveProvider` (or equivalent), so a future Fluctuate mode only swaps the source.
- **Pause-condition listener:** a hidden window and/or `SystemEvents` subscriptions that raise lock, display and suspend changes on the UI thread. Unsubscribe from static `SystemEvents` handlers on dispose to avoid leaks.
- **`AudioLeashContext` wiring:** feed default-device changes (already arriving via `OnDefaultDeviceChanged` and `SafeInvoke`), device state changes, pause-condition changes, settings toggles and startup into `KeepAliveState`, then act on its decision. Dispose the stream on exit, including the session-end and installer exit paths.
- **Settings form:** the card, the sub-page, and a `KeepAliveToggled(id, enabled)` style event that the context handles immediately, matching how `StartWithWindowsToggled` works.

### Performance requirements

- Use a **large buffer / latency** (builder's call; target around 200–500 ms) so the render thread wakes a few times per second, not every 10 ms. Check what NAudio's `WasapiOut` actually does with event-sync versus timer-sync at that latency, and pick whichever wakes least.
- **Zero allocations per buffer fill.** The silence source clears the provided buffer (`Array.Clear` / `Span.Clear`) and returns the count.
- When keep-alive isn't wanted (no enabled default, locked, display off), **no stream, no thread and no timer** should exist. The retry timer runs only while a stream is wanted but failing.
- After implementation, sanity-check it: Task Manager or Process Explorer with the stream running should show AudioLeash at roughly 0% CPU and flat memory over several minutes. Report what you see. A measured number isn't required, but don't claim it's light without looking.

## Constraints

- .NET 10, C# 14, WinForms, nullable enabled, `NAudio.Wasapi` 2.2.1 (already referenced). Per CLAUDE.md, look up NAudio's API with context7 before using it. Confirm what `WasapiOut` (or the lower-level `AudioClient` / `AudioRenderClient`) offers in 2.2.1 for shared mode, latency, event versus timer sync, and how `PlaybackStopped` reports errors.
- **Threading/COM:** `AudioLeashContext` creates its `MMDeviceEnumerator` on the STA UI thread. The render thread is separate. Make sure the `MMDevice` and audio client are created and used in a way NAudio supports across threads. This is a known NAudio pitfall that shows up as `InvalidCastException` when STA-created COM objects are used from another thread. Get the device from an enumerator, or initialise the stream, on whichever thread NAudio requires.
- Dispose every `MMDevice`, enumerator and stream (see `.claude/rules/csharp-patterns.md`).
- Tests: xUnit + NSubstitute, with no real audio stack. Unit-test `KeepAliveState` thoroughly, including every pause input, default changes, enable/disable, device going inactive, and the reconcile decisions. Add `SettingsService` tests for the new field (round-trip, sanitisation, preserving other fields, and an old file without the field).
- Update **README.md**: what keep-alive is for, how to turn it on, Zero mode only, the pause behaviour, and the volume-mixer note. Update **DEVELOPMENT.md** (architecture notes) and the **CLAUDE.md** project structure with the new files. Add "Fluctuate mode for keep-alive" to the DEVELOPMENT.md roadmap.
- **Release:** v2.0.0 is already tagged, so this ships as **2.1.0**. Bump the version wherever 2.0.0 was set in commit 710a8ae, and add a 2.1.0 section to RELEASE_NOTES.md. *(Design call.)*
- Work on `feature/keep-alive`. Don't merge to main until the user approves.

## Left to the builder

- Exact class names and file split, beyond the pure/thin separation above.
- The coalescing delay, retry interval, buffer latency, and whether to use `WasapiOut` or the lower-level `AudioClient`.
- The mechanism for detecting lock, display and suspend.
- Exact wording on the card and sub-page, and the back link's visual design (it should match the existing flat styling).
