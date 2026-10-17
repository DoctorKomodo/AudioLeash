# Device Priority: Design

**Date:** 2026-10-17
**Status:** Approved

## Intent

Today AudioLeash pins you to a single device that you picked by hand. With this feature you rank your devices once, and AudioLeash keeps you on the best one that's connected. It moves up when a better device appears, falls back when the current one goes away, and still lets you override it by hand. A manual pick stays in force until you return to priority order or that device disconnects.

The motivating example:

- Headphones are the everyday device.
- When the TV is connected over HDMI, audio should go to the TV automatically.
- If the user picks the Creative USB speakers by hand, AudioLeash keeps them there, even if a higher-ranked device connects. It stays on them until the user chooses "Return to priority order" or the speakers disconnect. *(Revised by the user after an earlier draft, where a higher-ranked device connecting also ended the manual pick. That rule was dropped as too complicated.)*

## Goals

1. Separate ranked priority lists for **playback** and **recording**. AudioLeash already keeps a separate selection for each, and each list works on its own.
2. AudioLeash keeps the default device on the **highest-ranked device that is currently connected**.
3. A manual pick from the tray is an **override** that lasts until the user cancels it or the device disconnects.
4. A small **settings window** for managing the lists.
5. Users who never set up a list see **no change in behaviour**.

## Non-goals

- No "pause AudioLeash" toggle. If you need it to stop, exit the app. (User's decision.)
- No profiles, hotkey cycling, communications-device handling or per-app routing. Those stay on the roadmap.
- No change to which Windows role is enforced. The app still reacts to the Multimedia role and sets the default endpoint the same way `PolicyConfigClient` does today.

## Anti-goals (it fails if…)

- …it switches on its own to a device the user never ranked.
- …any device connecting, of any rank, knocks the user off a manual pick.
- …an HDMI/eARC handshake flap makes it bounce between devices or send a burst of notifications. The TV is the headline use case, and HDMI is exactly where flapping happens. See the existing `DebounceMs` comment in `AudioLeashContext`.
- …existing users who upgrade find their saved device dropped or behaving differently before they've touched the new settings.

## Behaviour

These rules apply per flow (playback and recording each work independently).

### Terms

- **Ranked list:** an ordered list of device IDs. Position 1 is the highest priority.
- **Connected:** the endpoint is present and `DeviceState.Active`.
- **Target:** the device AudioLeash currently enforces as the Windows default.
- **Override:** a manual pick that is currently in force (priority mode). It is persisted across restarts.

### When the ranked list is non-empty (priority mode)

1. **Default target.** With no override, the target is the highest-ranked connected device. If no ranked device is connected, AudioLeash stays out of the way and enforces nothing until one connects.
2. **Enforcement.** When Windows changes the default away from the target, AudioLeash switches it back. This is the same as today's restore behaviour, including the `IsInternalChange` guard against feedback loops.
3. **Higher device connects.** With no override in force, when a ranked device that outranks the current target connects, AudioLeash switches to it. While an override is in force, nothing changes.
4. **Target disconnects.** AudioLeash falls back to the highest-ranked connected device that's left. If an override was in force, it ends.
5. **Manual pick.** Selecting any device in the tray menu, ranked or not, starts an override on that device. AudioLeash then enforces the override exactly as it enforces a target.
6. **What ends an override:**
   - the overridden device disconnects (after debouncing, rule 9). AudioLeash then falls back to the highest-ranked connected device.
   - the user chooses **"Return to priority order"** in the tray, which switches straight to the highest-ranked connected device.
   - A restart does **not** end it (user's decision). The override is persisted to `settings.json` and comes back on startup.
   - **Startup with the overridden device absent** *(design call)*: Windows audio devices can take a while to come up after boot (see the boot-time race noted under Profiles in DEVELOPMENT.md's roadmap). So a missing device at startup is not treated as an immediate disconnect. AudioLeash waits a short startup grace period (builder's call, a few seconds). If the device appears during that time, the override holds. If it doesn't, the override ends and AudioLeash falls back to the top connected ranked device, with the usual fallback notification.
7. **What does NOT end an override:** any device connecting or disconnecting, ranked or unranked, other than the overridden device itself disconnecting. Editing and saving the ranked list doesn't end it either.
8. **Unranked devices** are never chosen automatically. You can only reach them by a manual pick.
9. **Debouncing.** Connect/disconnect handling for *any ranked device* (not just the current target) must go through the same settle-then-act debounce the app uses today. A device only counts as having connected or disconnected once its state has stopped flapping. After the burst settles, AudioLeash re-evaluates the target once, and sends at most one notification.
10. **Notifications.** Use the existing balloon-tip style for: an automatic switch caused by priority ("Switched to LG TV (higher priority)"), and a fallback after a disconnect, including a fallback because the overridden device disconnected. A flap that ends where it started produces no notification, same as today.

### When the ranked list is empty (legacy mode)

Behaviour is exactly as today. A manual pick is persisted and enforced indefinitely, the unavailable-device handling works the same, and **"Clear Selection"** keeps its current meaning.

### Upgrade / migration

- Existing `settings.json` files have no ranked lists, so after upgrading the user is in legacy mode with their saved device, unchanged.
- In both modes the persisted selection means "the user's manual pick". In legacy mode it's enforced indefinitely, as today. In priority mode it's the override. The builder may reuse the existing `Selected*DeviceId`/`Name` fields for the override, or add new ones.
- When a flow goes from no list to a list on Save, its existing pick is cleared, so priority takes over immediately (see "What Save does to the current state").
- The settings file must stay backward compatible in the same way `SettingsService` handles the legacy `SelectedDeviceId` field today. A missing priority field means an empty list.

## Settings window

Approved layout: **"one list with a not-ranked section"** (mockup B).

- Opened from a new **"Settings…"** item in the tray menu.
- Three tabs: **Playback**, **Recording**, **General**.
- **Playback / Recording tabs:** a single list per tab.
  - Above a divider: ranked devices, in order, with their rank number.
  - Below the divider labelled *"Not ranked: AudioLeash won't switch to these on its own"*: every other device Windows knows about for that flow.
  - Each row shows the friendly name, the device description as a smaller second line, and a connected / not connected indicator.
  - Reordering: drag and drop, including across the divider to rank or unrank a device, plus ▲/▼ buttons that move the selected row. ▲/▼ across the divider ranks or unranks the device as well.
  - A ranked device that isn't currently connected stays in its place and is shown as "not connected". That means the ranked list must persist each device's friendly name (and description) so it can be shown while the device is absent, the same way the app already persists `SelectedPlaybackDeviceName`.
  - Newly seen devices appear under "Not ranked".
- **General tab:** "Start with Windows". This also stays in the tray menu.
- **Save / Cancel.** Nothing applies until Save. Cancel or closing the window discards the changes. When you click Save, AudioLeash re-evaluates the target for each flow immediately.
- **What Save does to the current state** (per flow; *these are design calls, not the user's decisions*):
  - **No list → list (legacy → priority):** the legacy pick is dropped, and the target becomes the top connected ranked device, the same as at startup. It does not turn into an override.
  - **Override in force, list edited:** the override stays in force. Only "Return to priority order" or the device disconnecting ends it. If the overridden device was unranked and has now been ranked, or the reverse, that changes nothing.
  - **List → empty (priority → legacy):** the device that is the Windows default at that moment becomes the persisted legacy pick and is enforced as it is today. An old legacy pick is never brought back.
- Follows the Windows light/dark theme, matching the existing `WindowsTheme` / `DarkMenuRenderer` support.
- Only one settings window at a time. Choosing "Settings…" again brings the open one to the front.

## Tray menu changes

- Devices that are ranked show their rank after the name, e.g. `Headphones  #2`.
- The device in force has the check mark, as today. When it's an override, its line also says *"manual"* (or similar).
- **"Return to priority order"** replaces "Clear Selection" in priority mode, and is only shown while an override is in force for at least one flow. *(Builder's call: one item that clears both flows, or one per flow. Pick whichever reads cleanly.)*
- In legacy mode (empty list), "Clear Selection" stays as it is today.
- **Mixed modes** (one flow has a list, the other doesn't; *design call*): each item applies only to flows in its own mode. "Clear Selection" appears if any flow is in legacy mode and clears only those flows. It must never touch a flow that has a list, so replace today's both-flows `SaveSelectedDeviceIds(null, null)` with per-flow saves. "Return to priority order" appears if any priority-mode flow has an override, and affects only those flows. Both items can show at once.
- New **"Settings…"** item. "Start with Windows" and "Exit" stay.
- The tray tooltip should reflect the current target, and whether it's an override.

## Constraints

- Follow `CLAUDE.md` and `.claude/rules/*`: .NET 10 WinForms, nullable enabled, dispose `MMDevice`/enumerators, marshal audio-thread callbacks to the UI thread via `SafeInvoke`, feature branch plus conventional commits.
- **Keep the decision logic pure and unit-tested.** It extends or sits beside `DeviceSelectionState`, with no NAudio or WinForms types, so xUnit can cover:
  - target selection from a ranked list plus a set of connected devices
  - override start and every override-ending condition in "What ends an override"
  - "any device connects while an override is in force → no change"
  - empty list → legacy behaviour unchanged
  - debounced flap → single outcome / no notification
  - startup with no persisted override → top connected ranked device
  - startup with a persisted override → override restored; override device absent past the grace period → fallback
- `SettingsService` gains the ranked lists (ID plus friendly name per entry, per flow), with tests that cover backward compatibility with today's files.
- The settings window itself isn't unit-tested (per CLAUDE.md). Any list-manipulation logic it needs (move up/down, move across the divider, merging known devices with the persisted list) should be extracted into a testable class.
- Update `README.md` (user-facing feature), `DEVELOPMENT.md` (architecture/structure, Already-shipped list), and the project-structure tree in `CLAUDE.md` for new files.
- Address the **known race** noted in `CLAUDE.md` (`OnDefaultDeviceChanged` reading `SelectedDeviceId` twice without a lock) as part of this work, since this feature rewrites that path. Snapshot the decision inputs once.

## Left to the builder

- How the pure state is structured: extend `DeviceSelectionState` or add a new `PriorityState` that wraps it.
- How "devices Windows knows about" is enumerated for the settings list (e.g. `DeviceState.Active | Unplugged | Disabled`, or `NotPresent` too), as long as unplugged-but-ranked devices still show.
- Exact wording of notifications and tray labels.
- Settings window sizing, control choice (`ListView`/`ListBox` owner-draw, etc.) and drag-and-drop implementation.
- Whether to clear the legacy single-device selection when a list becomes non-empty.
