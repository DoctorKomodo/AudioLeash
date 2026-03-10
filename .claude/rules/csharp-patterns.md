---
description: C# and Windows-specific coding patterns for AudioLeash
globs: "**/*.cs"
---

# C# Patterns

- Use nullable reference types (`#nullable enable`) — all new code must be null-safe
- Dispose all `IDisposable` objects — especially `MMDeviceEnumerator`, `MMDevice`, and `NotifyIcon`
- Use `using` declarations (C# 8+) rather than `using` blocks unless scope control is needed
- Prefer `async/await` over raw `Task.Run` + callbacks where it improves readability
- Target Windows only (`net10.0-windows`) — no cross-platform abstractions
- When interacting with Win32/COM APIs, check return values and `Marshal.GetLastWin32Error()`
- All UI interactions must occur on the UI/STA thread; marshal `IMMNotificationClient` callbacks via `Control.Invoke` (see `SafeInvoke` in `AudioLeashContext`)
