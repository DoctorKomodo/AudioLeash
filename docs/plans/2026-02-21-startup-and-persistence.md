# Startup Toggle + Settings Persistence Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a "Start with Windows" menu toggle (registry `Run` key) and JSON settings persistence so the selected audio device survives reboots.

**Architecture:** Two new service classes (`SettingsService`, `StartupService`) are injected into `AudioLeashContext`. `SettingsService` reads/writes `%AppData%\AudioLeash\settings.json` via `System.Text.Json`. `StartupService` manages `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Both accept constructor-injected paths for testability.

**Tech Stack:** C# 12 / .NET 8, `System.Text.Json` (BCL), `Microsoft.Win32.Registry` (BCL on Windows), xUnit, NSubstitute (already in test project).

---

## Prerequisites

Create and switch to a feature branch before touching any code:

```bash
git checkout -b feature/startup-and-persistence
```

---

## Task 1: Create `SettingsService` (TDD)

**Files:**
- Create: `AudioLeash/SettingsService.cs`
- Create: `AudioLeash.Tests/SettingsServiceTests.cs`

---

### Step 1: Write the failing tests

Create `AudioLeash.Tests/SettingsServiceTests.cs`:

```csharp
#nullable enable
using System;
using System.IO;
using AudioLeash;

namespace AudioLeash.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"AudioLeash-Test-{Guid.NewGuid()}");

    private SettingsService Svc() => new(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_WhenFileAbsent_ReturnsNull()
    {
        Assert.Null(Svc().LoadSelectedDeviceId());
    }

    [Fact]
    public void Load_AfterSaveWithId_ReturnsSameId()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceId("device-123");
        Assert.Equal("device-123", svc.LoadSelectedDeviceId());
    }

    [Fact]
    public void Load_AfterSaveNull_ReturnsNull()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceId("device-123");
        svc.SaveSelectedDeviceId(null);
        Assert.Null(svc.LoadSelectedDeviceId());
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsNull()
    {
        var svc = Svc();
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "not valid json{{{{");
        Assert.Null(svc.LoadSelectedDeviceId());
    }
}
```

### Step 2: Run — verify they fail

```bash
dotnet test AudioLeash.sln --filter "FullyQualifiedName~SettingsServiceTests"
```

Expected: **compilation error** — `SettingsService` does not exist yet.

---

### Step 3: Implement `SettingsService`

Create `AudioLeash/SettingsService.cs`:

```csharp
#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioLeash;

/// <summary>
/// Persists user settings to %AppData%\AudioLeash\settings.json.
/// All operations are best-effort: failures are silently swallowed.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _directory;
    private string FilePath => Path.Combine(_directory, "settings.json");

    /// <summary>Production constructor — uses %AppData%\AudioLeash\.</summary>
    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioLeash"))
    { }

    /// <summary>Test constructor — uses the provided directory.</summary>
    internal SettingsService(string directory) => _directory = directory;

    public string? LoadSelectedDeviceId()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings?.SelectedDeviceId;
        }
        catch
        {
            return null;
        }
    }

    public void SaveSelectedDeviceId(string? id)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var settings = new AppSettings(id);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch { /* best-effort — do not crash the app over persistence */ }
    }

    private sealed record AppSettings(string? SelectedDeviceId);
}
```

### Step 4: Run — verify tests pass

```bash
dotnet test AudioLeash.sln --filter "FullyQualifiedName~SettingsServiceTests"
```

Expected: **4 tests pass.**

### Step 5: Run full test suite — verify no regressions

```bash
dotnet test AudioLeash.sln
```

Expected: all tests pass.

### Step 6: Commit

```bash
git add AudioLeash/SettingsService.cs AudioLeash.Tests/SettingsServiceTests.cs
git commit -m "feat: add SettingsService for JSON settings persistence"
```

---

## Task 2: Create `StartupService` (TDD)

**Files:**
- Create: `AudioLeash/StartupService.cs`
- Create: `AudioLeash.Tests/StartupServiceTests.cs`

---

### Step 1: Write the failing tests

Create `AudioLeash.Tests/StartupServiceTests.cs`:

```csharp
#nullable enable
using Microsoft.Win32;
using AudioLeash;

namespace AudioLeash.Tests;

/// <summary>
/// Tests use a dedicated registry subkey to avoid touching the real Run key.
/// The key is deleted in Dispose().
/// </summary>
public sealed class StartupServiceTests : IDisposable
{
    // Use a private, deletable test key well away from the real Run key.
    private const string TestKeyPath = @"Software\AudioLeash-Tests\Run";

    private StartupService Svc() => new(TestKeyPath);

    public void Dispose()
    {
        // Clean up the whole test subtree.
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\AudioLeash-Tests", throwOnMissingSubKey: false);
    }

    [Fact]
    public void IsEnabled_WhenValueAbsent_ReturnsFalse()
    {
        Assert.False(Svc().IsEnabled);
    }

    [Fact]
    public void Enable_ThenIsEnabled_ReturnsTrue()
    {
        var svc = Svc();
        svc.Enable(@"C:\test\AudioLeash.exe");
        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public void Disable_AfterEnable_IsEnabledReturnsFalse()
    {
        var svc = Svc();
        svc.Enable(@"C:\test\AudioLeash.exe");
        svc.Disable();
        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public void Disable_WhenNeverEnabled_DoesNotThrow()
    {
        var svc = Svc();
        var ex = Record.Exception(() => svc.Disable());
        Assert.Null(ex);
    }
}
```

### Step 2: Run — verify they fail

```bash
dotnet test AudioLeash.sln --filter "FullyQualifiedName~StartupServiceTests"
```

Expected: **compilation error** — `StartupService` does not exist yet.

---

### Step 3: Implement `StartupService`

Create `AudioLeash/StartupService.cs`:

```csharp
#nullable enable
using Microsoft.Win32;

namespace AudioLeash;

/// <summary>
/// Manages the HKCU\...\Run registry entry that launches AudioLeash at login.
/// </summary>
public sealed class StartupService
{
    private const string DefaultRunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AudioLeash";

    private readonly string _runKeyPath;

    /// <summary>Production constructor — uses the real Run key.</summary>
    public StartupService() : this(DefaultRunKeyPath) { }

    /// <summary>Test constructor — accepts an injectable key path.</summary>
    internal StartupService(string runKeyPath) => _runKeyPath = runKeyPath;

    /// <summary>True when the AudioLeash Run value exists under the configured key.</summary>
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            return key?.GetValue(AppName) is not null;
        }
    }

    /// <summary>Writes the Run value pointing to <paramref name="exePath"/>.</summary>
    public void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
        key.SetValue(AppName, exePath);
    }

    /// <summary>Removes the Run value. Safe to call when already disabled.</summary>
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
```

### Step 4: Run — verify tests pass

```bash
dotnet test AudioLeash.sln --filter "FullyQualifiedName~StartupServiceTests"
```

Expected: **4 tests pass.**

### Step 5: Run full test suite — verify no regressions

```bash
dotnet test AudioLeash.sln
```

Expected: all tests pass.

### Step 6: Commit

```bash
git add AudioLeash/StartupService.cs AudioLeash.Tests/StartupServiceTests.cs
git commit -m "feat: add StartupService for Windows Run-key startup management"
```

---

## Task 3: Wire `SettingsService` into `AudioLeashContext`

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs`

This task adds persistence to existing selection/clear flows and seeds the selection on startup from the saved preference.

---

### Step 1: Add field declarations

In `AudioLeashContext.cs`, add two new `readonly` fields alongside the existing ones (after `_notificationClient`):

```csharp
private readonly SettingsService  _settingsService;
private readonly StartupService   _startupService;
```

### Step 2: Initialise services in the constructor

In the constructor body, before `RefreshDeviceList()`, add:

```csharp
_settingsService = new SettingsService();
_startupService  = new StartupService();
```

### Step 3: Replace the startup device-seeding logic

Find the current seed block (lines 49–51):

```csharp
// Seed the selection with whatever Windows currently uses as the default.
using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
if (defaultDevice is not null)
    _selection.SelectDevice(defaultDevice.ID);
```

Replace it with:

```csharp
// Try to restore the previously selected device; fall back to Windows current default.
string? savedId = _settingsService.LoadSelectedDeviceId();
if (savedId is not null)
{
    var active = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
    bool available = active.Any(d => d.ID == savedId);
    foreach (var d in active) d.Dispose();

    if (available)
    {
        _selection.SelectDevice(savedId);
    }
}

if (_selection.SelectedDeviceId is null)
{
    // No saved preference (or device unavailable) — seed from Windows current default.
    using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    if (defaultDevice is not null)
        _selection.SelectDevice(defaultDevice.ID);
}
```

### Step 4: Persist selection when user picks a device

In `DeviceMenuItem_Click`, after `_selection.SelectDevice(deviceId)` (line ~149), add:

```csharp
_settingsService.SaveSelectedDeviceId(deviceId);
```

### Step 5: Clear persisted selection when user clears

In `ClearSelection_Click`, after `_selection.ClearSelection()` (line ~171), add:

```csharp
_settingsService.SaveSelectedDeviceId(null);
```

### Step 6: Clear persisted selection when device becomes unavailable

In `OnDefaultDeviceChanged`, in the `RestoreDecision.ClearSelection` branch, add the save call inside `SafeInvoke` before the balloon tip:

Find:
```csharp
case RestoreDecision.ClearSelection:
    _selection.ClearSelection();
    SafeInvoke(() =>
    {
        _trayIcon.ShowBalloonTip(
```

Change to:
```csharp
case RestoreDecision.ClearSelection:
    _selection.ClearSelection();
    SafeInvoke(() =>
    {
        _settingsService.SaveSelectedDeviceId(null);
        _trayIcon.ShowBalloonTip(
```

### Step 7: Build to verify no errors

```bash
dotnet build AudioLeash.sln
```

Expected: **Build succeeded, 0 errors.**

### Step 8: Run full test suite

```bash
dotnet test AudioLeash.sln
```

Expected: all tests pass.

### Step 9: Commit

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "feat: wire SettingsService into AudioLeashContext for startup persistence"
```

---

## Task 4: Wire `StartupService` into `AudioLeashContext` (menu item)

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs`

---

### Step 1: Add the "Start with Windows" menu item in `RefreshDeviceList`

Find the block that adds the final separator and Exit item:

```csharp
_contextMenu.Items.Add(new ToolStripSeparator());

var exitItem = new ToolStripMenuItem("Exit");
exitItem.Click += Exit_Click;
_contextMenu.Items.Add(exitItem);
```

Replace with:

```csharp
_contextMenu.Items.Add(new ToolStripSeparator());

var startupItem = new ToolStripMenuItem("Start with Windows")
{
    Checked = _startupService.IsEnabled,
};
startupItem.Click += StartupItem_Click;
_contextMenu.Items.Add(startupItem);

_contextMenu.Items.Add(new ToolStripSeparator());

var exitItem = new ToolStripMenuItem("Exit");
exitItem.Click += Exit_Click;
_contextMenu.Items.Add(exitItem);
```

### Step 2: Add the click handler

Add this method alongside the other `_Click` handlers (e.g. after `ClearSelection_Click`):

```csharp
private void StartupItem_Click(object? sender, EventArgs e)
{
    if (_startupService.IsEnabled)
        _startupService.Disable();
    else
        _startupService.Enable(Environment.ProcessPath ?? Application.ExecutablePath);

    RefreshDeviceList();
}
```

### Step 3: Build to verify no errors

```bash
dotnet build AudioLeash.sln
```

Expected: **Build succeeded, 0 errors.**

### Step 4: Run full test suite

```bash
dotnet test AudioLeash.sln
```

Expected: all tests pass.

### Step 5: Commit

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "feat: add Start with Windows toggle to tray menu"
```

---

## Task 5: Update `README.md`

**Files:**
- Modify: `README.md`

---

### Step 1: Add new feature entries

In the **Current Feature Set** section, append two new numbered entries after the last item (item 12):

```markdown
### 13. Start with Windows
- A **"Start with Windows"** item in the tray menu registers or removes AudioLeash from the Windows `HKCU\...\Run` registry key.
- A checkmark indicates it is currently registered.
- Clicking the item toggles registration.

### 14. Settings Persistence
- The user-selected audio device is saved to `%AppData%\AudioLeash\settings.json`.
- On next launch, AudioLeash restores the saved selection automatically (if the device is still available).
- Clearing the selection also removes the saved preference.
```

### Step 2: Remove from future development list

In the **Ideas for Future Development** section, remove (or mark done) the two items now implemented:

```markdown
- **Windows startup** — Add/remove a registry `Run` key to launch the app at login.
...
- **Settings persistence** — Save selected device across restarts (JSON or registry).
```

Replace them with:

```markdown
- ~~**Windows startup**~~ — ✔ Implemented (registry `Run` key toggle in tray menu).
...
- ~~**Settings persistence**~~ — ✔ Implemented (JSON file in `%AppData%\AudioLeash\`).
```

### Step 3: Commit

```bash
git add README.md
git commit -m "docs: update README for startup toggle and settings persistence features"
```

---

## Task 6: Code Review

Per the project workflow, run two sequential subagent reviews:

### Step 1: Big-picture review (subagent)

Spawn a subagent with this prompt:

> Review the changes on feature branch `feature/startup-and-persistence` against the `main` branch. Focus on the big picture: how are `SettingsService` and `StartupService` used from `AudioLeashContext`? Are there correctness issues in the startup seeding logic? Does the registry key toggle work correctly on Enable/Disable? Are there lifecycle or disposal concerns? Reference the design doc at `docs/plans/2026-02-21-startup-and-persistence-design.md`.

### Step 2: Standard code review (subagent)

Spawn a second subagent using `superpowers:requesting-code-review`.

### Step 3: Address findings

Address any non-trivial findings. Commit fixes to the feature branch.

---

## Task 7: Final Verification + Merge

### Step 1: Run full test suite one last time

```bash
dotnet test AudioLeash.sln
```

Expected: all tests pass.

### Step 2: Build release

```bash
dotnet build AudioLeash.sln -c Release
```

Expected: **Build succeeded, 0 errors.**

### Step 3: Get merge approval from user

Present the branch diff summary and ask the user to approve the merge.

### Step 4: Merge and clean up (only after user approval)

```bash
git checkout main
git merge --no-ff feature/startup-and-persistence -m "feat: add startup toggle and settings persistence"
git push
git branch -d feature/startup-and-persistence
```
