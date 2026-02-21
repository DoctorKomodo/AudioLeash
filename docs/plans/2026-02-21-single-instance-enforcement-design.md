# Single-Instance Enforcement — Design

**Date:** 2026-02-21

## Problem

Launching AudioLeash a second time while it is already running creates a duplicate tray icon. The user ends up with two independent instances monitoring audio device changes, which causes double balloon tips and unpredictable restore behaviour.

## Goal

Ensure only one instance of AudioLeash runs at a time. If a second instance is launched, it exits immediately and silently.

## Approach

Acquire a named system `Mutex` at the very start of `Main`, before any WinForms or audio initialisation. If the mutex is already owned by another process, exit immediately.

- **Mutex name:** `Global\AudioLeash`
  The `Global\` prefix makes it session-global, so it works correctly across UAC elevation boundaries and Fast User Switching.
- **No IPC required** — silent exit means the second instance needs no communication with the first.
- **Mutex lifetime** — held via a `using` declaration for the entire process lifetime; released automatically on exit or crash.

## Changes

Only `Program.cs` is modified — no changes to `AudioLeashContext` or any other file.

```csharp
[STAThread]
static void Main()
{
    using var mutex = new Mutex(initiallyOwned: false, "Global\\AudioLeash", out bool created);
    if (!created || !mutex.WaitOne(0))
        return; // Another instance is already running — exit silently.

    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new AudioLeashContext());
}
```

## Out of Scope

- Signalling the existing instance to open its menu (not requested).
- Showing a balloon tip from the second instance (not requested).
