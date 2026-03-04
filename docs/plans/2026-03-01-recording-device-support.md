# Recording Device (Microphone) Support — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extend AudioLeash to leash recording (capture) devices independently alongside playback (render) devices, preventing Windows from switching either without permission.

**Architecture:** Two `DeviceSelectionState` instances (`_playbackState`, `_captureState`) managed by `AudioLeashContext`. `SettingsService` evolves from a single device ID to separate playback/capture IDs with backward-compatible migration. The notification client routes events by `DataFlow` to the correct state machine. The tray menu shows two sections with bold headers.

**Tech Stack:** C# 14 / .NET 10, WinForms (system tray), NAudio.Wasapi 2.2.1 (`MMDeviceEnumerator`, `DataFlow.Capture`)

**Design doc:** `docs/plans/2026-03-01-recording-device-support-design.md`

---

### Task 1: Evolve SettingsService to Support Two Device IDs

**Files:**
- Modify: `AudioLeash/SettingsService.cs`
- Modify: `AudioLeash.Tests/SettingsServiceTests.cs`

**Step 1: Write the failing tests**

Add these tests to `AudioLeash.Tests/SettingsServiceTests.cs`:

```csharp
// ── Playback + Capture persistence ──────────────────────────────────

[Fact]
public void LoadPlaybackId_WhenFileAbsent_ReturnsNull()
{
    Assert.Null(Svc().LoadSelectedPlaybackDeviceId());
}

[Fact]
public void LoadCaptureId_WhenFileAbsent_ReturnsNull()
{
    Assert.Null(Svc().LoadSelectedCaptureDeviceId());
}

[Fact]
public void SaveAndLoadPlaybackId_RoundTrips()
{
    var svc = Svc();
    svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: null);
    Assert.Equal("pb-123", svc.LoadSelectedPlaybackDeviceId());
    Assert.Null(svc.LoadSelectedCaptureDeviceId());
}

[Fact]
public void SaveAndLoadCaptureId_RoundTrips()
{
    var svc = Svc();
    svc.SaveSelectedDeviceIds(playbackId: null, captureId: "cap-456");
    Assert.Null(svc.LoadSelectedPlaybackDeviceId());
    Assert.Equal("cap-456", svc.LoadSelectedCaptureDeviceId());
}

[Fact]
public void SaveAndLoadBothIds_RoundTrips()
{
    var svc = Svc();
    svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: "cap-456");
    Assert.Equal("pb-123", svc.LoadSelectedPlaybackDeviceId());
    Assert.Equal("cap-456", svc.LoadSelectedCaptureDeviceId());
}

[Fact]
public void SavePlaybackId_PreservesCaptureId()
{
    var svc = Svc();
    svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: "cap-456");
    svc.SaveSelectedPlaybackDeviceId("pb-new");
    Assert.Equal("pb-new", svc.LoadSelectedPlaybackDeviceId());
    Assert.Equal("cap-456", svc.LoadSelectedCaptureDeviceId());
}

[Fact]
public void SaveCaptureId_PreservesPlaybackId()
{
    var svc = Svc();
    svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: "cap-456");
    svc.SaveSelectedCaptureDeviceId("cap-new");
    Assert.Equal("pb-123", svc.LoadSelectedPlaybackDeviceId());
    Assert.Equal("cap-new", svc.LoadSelectedCaptureDeviceId());
}

// ── Backward compatibility (migration from old single-field format) ──

[Fact]
public void LoadPlaybackId_MigratesOldSelectedDeviceIdField()
{
    // Simulate old settings format: { "selectedDeviceId": "old-device" }
    var svc = Svc();
    Directory.CreateDirectory(_tempDir);
    File.WriteAllText(
        Path.Combine(_tempDir, "settings.json"),
        """{"selectedDeviceId": "old-device"}""");

    Assert.Equal("old-device", svc.LoadSelectedPlaybackDeviceId());
    Assert.Null(svc.LoadSelectedCaptureDeviceId());
}

[Fact]
public void LoadPlaybackId_NewFieldTakesPrecedenceOverOldField()
{
    // If both old and new fields exist, new field wins
    var svc = Svc();
    Directory.CreateDirectory(_tempDir);
    File.WriteAllText(
        Path.Combine(_tempDir, "settings.json"),
        """{"selectedDeviceId": "old-device", "selectedPlaybackDeviceId": "new-device"}""");

    Assert.Equal("new-device", svc.LoadSelectedPlaybackDeviceId());
}

// ── Clear both ──────────────────────────────────────────────────────

[Fact]
public void ClearAll_ClearsBothIds()
{
    var svc = Svc();
    svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: "cap-456");
    svc.SaveSelectedDeviceIds(playbackId: null, captureId: null);
    Assert.Null(svc.LoadSelectedPlaybackDeviceId());
    Assert.Null(svc.LoadSelectedCaptureDeviceId());
    Assert.True(svc.HasSettingsFile);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test AudioLeash.sln`
Expected: FAIL — new methods don't exist yet.

**Step 3: Implement the new SettingsService**

Replace the full contents of `AudioLeash/SettingsService.cs` with:

```csharp
#nullable enable
using System;
using System.IO;
using System.Text.Json;

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

    /// <summary>
    /// Returns <c>true</c> if the settings file exists on disk, regardless of its contents.
    /// </summary>
    public bool HasSettingsFile => File.Exists(FilePath);

    // ── Legacy API (kept for backward compatibility during migration) ──

    /// <summary>
    /// Loads the selected device ID from the old single-field format.
    /// Delegates to <see cref="LoadSelectedPlaybackDeviceId"/> for the new format.
    /// </summary>
    public string? LoadSelectedDeviceId() => LoadSelectedPlaybackDeviceId();

    /// <summary>
    /// Saves using the old single-field name. Delegates to the new API,
    /// preserving any existing capture selection.
    /// </summary>
    public void SaveSelectedDeviceId(string? id) => SaveSelectedPlaybackDeviceId(id);

    // ── New API ─────────────────────────────────────────────────────────

    public string? LoadSelectedPlaybackDeviceId()
    {
        var settings = LoadSettings();
        // New field takes precedence; fall back to legacy field for migration
        return settings?.SelectedPlaybackDeviceId ?? settings?.SelectedDeviceId;
    }

    public string? LoadSelectedCaptureDeviceId()
    {
        return LoadSettings()?.SelectedCaptureDeviceId;
    }

    public void SaveSelectedPlaybackDeviceId(string? id)
    {
        var existing = LoadSettings();
        SaveSettings(new AppSettings(
            SelectedDeviceId: null,  // clear legacy field
            SelectedPlaybackDeviceId: id,
            SelectedCaptureDeviceId: existing?.SelectedCaptureDeviceId));
    }

    public void SaveSelectedCaptureDeviceId(string? id)
    {
        var existing = LoadSettings();
        SaveSettings(new AppSettings(
            SelectedDeviceId: null,  // clear legacy field
            SelectedPlaybackDeviceId: existing?.SelectedPlaybackDeviceId
                                     ?? existing?.SelectedDeviceId,
            SelectedCaptureDeviceId: id));
    }

    public void SaveSelectedDeviceIds(string? playbackId, string? captureId)
    {
        SaveSettings(new AppSettings(
            SelectedDeviceId: null,
            SelectedPlaybackDeviceId: playbackId,
            SelectedCaptureDeviceId: captureId));
    }

    // ── Internal helpers ────────────────────────────────────────────────

    private AppSettings? LoadSettings()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception) { /* best-effort */ }
    }

    private sealed record AppSettings(
        string? SelectedDeviceId,
        string? SelectedPlaybackDeviceId,
        string? SelectedCaptureDeviceId);
}
```

**Step 4: Verify existing tests still pass alongside new ones**

Run: `dotnet test AudioLeash.sln`
Expected: ALL PASS — old tests use `LoadSelectedDeviceId()`/`SaveSelectedDeviceId()` which delegate to the new API.

**Step 5: Commit**

```bash
git add AudioLeash/SettingsService.cs AudioLeash.Tests/SettingsServiceTests.cs
git commit -m "feat: extend SettingsService to persist playback and capture device IDs

Evolves AppSettings record to hold separate playback/capture device IDs.
Backward-compatible: migrates old single-field format on load.
Legacy API preserved as thin delegation to new methods."
```

---

### Task 2: Extend AudioNotificationClient to Route Capture Events

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs` (lines 444-465 — `AudioNotificationClient` inner class)

**Step 1: Update AudioNotificationClient to accept and route both DataFlows**

In `AudioLeashContext.cs`, replace the `AudioNotificationClient` class (lines 444-465) with:

```csharp
private sealed class AudioNotificationClient : IMMNotificationClient
{
    private readonly Action<DataFlow, string> _onDefaultChanged;

    internal AudioNotificationClient(Action<DataFlow, string> onDefaultChanged)
        => _onDefaultChanged = onDefaultChanged;

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // React to both playback and capture default changes for Multimedia role.
        // Windows fires this for Console, Multimedia, and Communications separately —
        // filtering to Multimedia prevents triple-firing.
        if (role == Role.Multimedia &&
            (flow == DataFlow.Render || flow == DataFlow.Capture))
        {
            _onDefaultChanged(flow, defaultDeviceId);
        }
    }

    // Required by IMMNotificationClient; AudioLeash does not act on these.
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
```

**Step 2: Update the constructor callback registration (line 62)**

Change:
```csharp
_notificationClient = new AudioNotificationClient(OnDefaultDeviceChanged);
```
To:
```csharp
_notificationClient = new AudioNotificationClient(OnDefaultDeviceChanged);
```

And update the `OnDefaultDeviceChanged` method signature (line 270) from:
```csharp
private void OnDefaultDeviceChanged(string newDefaultId)
```
To:
```csharp
private void OnDefaultDeviceChanged(DataFlow flow, string newDefaultId)
```

For now, at the top of the method body, add an early return to preserve existing behavior until Task 3:
```csharp
if (flow != DataFlow.Render) return;
```

**Step 3: Build to verify no compilation errors**

Run: `dotnet build AudioLeash.sln`
Expected: BUILD SUCCEEDED

**Step 4: Run tests to verify no regressions**

Run: `dotnet test AudioLeash.sln`
Expected: ALL PASS

**Step 5: Commit**

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "refactor: pass DataFlow through notification callback

AudioNotificationClient now forwards both Render and Capture events
with the DataFlow parameter. Capture events are ignored for now (early
return) — will be handled in the next task."
```

---

### Task 3: Add Second State Machine and Wire Up Capture Logic

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs`

This is the core task. It adds `_captureState`, updates the constructor to restore capture selection on startup, and wires `OnDefaultDeviceChanged` to route capture events to the capture state machine.

**Step 1: Add the capture state field**

At line 22, after `private readonly DeviceSelectionState    _selection;`, add:
```csharp
private readonly DeviceSelectionState    _captureSelection;
```

At line 31, after `_selection    = new DeviceSelectionState();`, add:
```csharp
_captureSelection = new DeviceSelectionState();
```

**Step 2: Update the constructor to restore capture selection on startup**

After the existing playback restoration block (around line 111, after the closing `}` of the `if (savedId is null) ... else ...` block), add capture device restoration:

```csharp
// Restore capture (recording) device selection.
string? savedCaptureId = _settingsService.LoadSelectedCaptureDeviceId();
if (savedCaptureId is not null)
{
    var activeCaptureDevices = _enumerator
        .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
    var savedCaptureDevice = activeCaptureDevices.FirstOrDefault(d => d.ID == savedCaptureId);
    bool captureAvailable = savedCaptureDevice is not null;
    foreach (var d in activeCaptureDevices) d.Dispose();

    if (captureAvailable)
    {
        _captureSelection.SelectDevice(savedCaptureId);
    }
    else
    {
        _settingsService.SaveSelectedCaptureDeviceId(null);
    }
}
```

**Step 3: Update OnDefaultDeviceChanged to route by DataFlow**

Replace the entire `OnDefaultDeviceChanged` method with a version that selects the correct state machine and DataFlow based on the `flow` parameter:

```csharp
private void OnDefaultDeviceChanged(DataFlow flow, string newDefaultId)
{
    var selection = flow == DataFlow.Render ? _selection : _captureSelection;

    bool isSelectedAvailable = false;
    if (selection.SelectedDeviceId is not null)
    {
        var activeDevices = _enumerator
            .EnumerateAudioEndPoints(flow, DeviceState.Active)
            .ToList();
        isSelectedAvailable = activeDevices.Any(d => d.ID == selection.SelectedDeviceId);
        foreach (var d in activeDevices) d.Dispose();
    }

    var decision = selection.EvaluateDefaultChange(newDefaultId, isSelectedAvailable);
    string flowLabel = flow == DataFlow.Render ? "Audio Device" : "Recording Device";

    switch (decision)
    {
        case RestoreDecision.NoAction:
            return;

        case RestoreDecision.ClearSelection:
            selection.ClearSelection();
            SafeInvoke(() =>
            {
                if (flow == DataFlow.Render)
                    _settingsService.SaveSelectedPlaybackDeviceId(null);
                else
                    _settingsService.SaveSelectedCaptureDeviceId(null);
                UpdateTrayTooltip();
                _trayIcon.ShowBalloonTip(
                    timeout:  3000,
                    tipTitle: $"{flowLabel} Unavailable",
                    tipText:  $"Your selected {flowLabel.ToLower()} is no longer available. Selection cleared.",
                    tipIcon:  ToolTipIcon.Warning);
                RefreshDeviceList();
            });
            break;

        case RestoreDecision.Restore:
            selection.IsInternalChange = true;
            string restoreId = selection.SelectedDeviceId!;

            try
            {
                SafeInvoke(() =>
                {
                    try
                    {
                        _policyConfig.SetDefaultEndpoint(restoreId);

                        var restoreDevices = _enumerator
                            .EnumerateAudioEndPoints(flow, DeviceState.Active)
                            .ToList();
                        string restoredName = restoreDevices
                            .FirstOrDefault(d => d.ID == restoreId)?.FriendlyName ?? restoreId;
                        foreach (var d in restoreDevices) d.Dispose();

                        UpdateTrayTooltip();

                        _trayIcon.ShowBalloonTip(
                            timeout:  3000,
                            tipTitle: $"{flowLabel} Restored",
                            tipText:  $"Switched back to: {restoredName}",
                            tipIcon:  ToolTipIcon.Info);
                        RefreshDeviceList();
                    }
                    catch (Exception ex)
                    {
                        _trayIcon.ShowBalloonTip(
                            timeout:  3000,
                            tipTitle: "Restore Failed",
                            tipText:  $"Could not restore {flowLabel.ToLower()}:\n{ex.Message}",
                            tipIcon:  ToolTipIcon.Error);
                    }
                    finally
                    {
                        selection.IsInternalChange = false;
                    }
                });
            }
            catch
            {
                selection.IsInternalChange = false;
            }
            break;
    }
}
```

**Step 4: Build and run tests**

Run: `dotnet build AudioLeash.sln && dotnet test AudioLeash.sln`
Expected: BUILD SUCCEEDED, ALL PASS

**Step 5: Commit**

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "feat: add capture device state machine and event routing

Adds _captureSelection state machine alongside existing _selection.
Restores saved capture device on startup. OnDefaultDeviceChanged routes
events by DataFlow to the correct state machine. Balloon tips and
settings persistence differentiate playback vs recording."
```

---

### Task 4: Update Tray Menu with Playback and Recording Sections

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs` — `RefreshDeviceList()`, `DeviceMenuItem_Click()`, `ClearSelection_Click()`, `UpdateTrayTooltip()`

**Step 1: Rewrite RefreshDeviceList() with two sections**

Replace `RefreshDeviceList()` (lines 131-197) with:

```csharp
private void RefreshDeviceList()
{
    foreach (ToolStripItem item in _contextMenu.Items)
    {
        if (item is ToolStripMenuItem { Tag: MMDevice d })
            d.Dispose();
    }
    _contextMenu.Items.Clear();

    // ── Playback section ────────────────────────────────────────────
    AddDeviceSection(
        DataFlow.Render,
        "Playback",
        _selection);

    // ── Recording section ───────────────────────────────────────────
    AddDeviceSection(
        DataFlow.Capture,
        "Recording",
        _captureSelection);

    // ── Actions ─────────────────────────────────────────────────────
    _contextMenu.Items.Add(new ToolStripSeparator());

    var clearItem = new ToolStripMenuItem("Clear Selection  (disable auto-restore)")
    {
        Enabled = _selection.SelectedDeviceId is not null
               || _captureSelection.SelectedDeviceId is not null,
    };
    clearItem.Click += ClearSelection_Click;
    _contextMenu.Items.Add(clearItem);

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
}

private void AddDeviceSection(
    DataFlow flow,
    string sectionLabel,
    DeviceSelectionState selection)
{
    // Bold section header (non-clickable)
    var header = new ToolStripMenuItem(sectionLabel)
    {
        Enabled = false,
        Font = new Font(_contextMenu.Font, FontStyle.Bold),
    };
    _contextMenu.Items.Add(header);
    _contextMenu.Items.Add(new ToolStripSeparator());

    var devices = _enumerator
        .EnumerateAudioEndPoints(flow, DeviceState.Active)
        .OrderBy(d => d.FriendlyName)
        .ToList();

    MMDevice? defaultDevice = null;
    try { defaultDevice = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); }
    catch (System.Runtime.InteropServices.COMException) { /* no default device for this flow */ }
    string? defaultId = defaultDevice?.ID;
    defaultDevice?.Dispose();

    if (devices.Count == 0)
    {
        _contextMenu.Items.Add(new ToolStripMenuItem("No devices available") { Enabled = false });
    }
    else
    {
        foreach (var device in devices)
        {
            bool isUserSelected   = selection.SelectedDeviceId == device.ID;
            bool isWindowsDefault = defaultId == device.ID;

            string label = device.FriendlyName;
            if (isWindowsDefault && !isUserSelected)
                label += "  (Windows default)";

            var menuItem = new ToolStripMenuItem(label)
            {
                Tag     = (device, flow),
                Checked = isUserSelected,
            };
            menuItem.Click += DeviceMenuItem_Click;
            _contextMenu.Items.Add(menuItem);
        }
    }
}
```

**Step 2: Update DeviceMenuItem_Click to handle both flows**

Replace `DeviceMenuItem_Click` (lines 199-232) with:

```csharp
private void DeviceMenuItem_Click(object? sender, EventArgs e)
{
    if (sender is not ToolStripMenuItem menuItem) return;
    if (menuItem.Tag is not (MMDevice device, DataFlow flow)) return;

    var selection = flow == DataFlow.Render ? _selection : _captureSelection;

    try
    {
        selection.IsInternalChange = true;

        string deviceId   = device.ID;
        string deviceName = device.FriendlyName;
        _policyConfig.SetDefaultEndpoint(deviceId);

        selection.SelectDevice(deviceId);

        if (flow == DataFlow.Render)
            _settingsService.SaveSelectedPlaybackDeviceId(deviceId);
        else
            _settingsService.SaveSelectedCaptureDeviceId(deviceId);

        UpdateTrayTooltip();

        string flowLabel = flow == DataFlow.Render ? "output" : "input";
        _trayIcon.ShowBalloonTip(
            timeout:  2500,
            tipTitle: flow == DataFlow.Render ? "Audio Device Selected" : "Recording Device Selected",
            tipText:  $"Default {flowLabel}: {deviceName}\n\nAuto-restore is now active.",
            tipIcon:  ToolTipIcon.Info);

        RefreshDeviceList();
    }
    catch (Exception ex)
    {
        ShowError($"Could not set device:\n{ex.Message}");
    }
    finally
    {
        selection.IsInternalChange = false;
    }
}
```

**Step 3: Update ClearSelection_Click to clear both**

Replace `ClearSelection_Click` (lines 234-247) with:

```csharp
private void ClearSelection_Click(object? sender, EventArgs e)
{
    _selection.ClearSelection();
    _captureSelection.ClearSelection();
    _settingsService.SaveSelectedDeviceIds(null, null);
    UpdateTrayTooltip();

    _trayIcon.ShowBalloonTip(
        timeout:  2500,
        tipTitle: "Selection Cleared",
        tipText:  "Auto-restore disabled. Select a device to re-enable.",
        tipIcon:  ToolTipIcon.Info);

    RefreshDeviceList();
}
```

**Step 4: Update UpdateTrayTooltip() to show both devices**

Replace the `UpdateTrayTooltip` method (lines 391-397) with:

```csharp
private void UpdateTrayTooltip()
{
    string? playbackName = GetDeviceName(DataFlow.Render, _selection.SelectedDeviceId);
    string? captureName  = GetDeviceName(DataFlow.Capture, _captureSelection.SelectedDeviceId);

    string text;
    if (playbackName is null && captureName is null)
        text = "AudioLeash";
    else if (captureName is null)
        text = $"AudioLeash\nPlayback: {playbackName}";
    else if (playbackName is null)
        text = $"AudioLeash\nRecording: {captureName}";
    else
        text = $"AudioLeash\nPlayback: {playbackName}\nRecording: {captureName}";

    _trayIcon.Text = text.Length > 63 ? text[..62] + "…" : text;
}

private string? GetDeviceName(DataFlow flow, string? deviceId)
{
    if (deviceId is null) return null;
    try
    {
        var devices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
        string? name = devices.FirstOrDefault(d => d.ID == deviceId)?.FriendlyName;
        foreach (var d in devices) d.Dispose();
        return name;
    }
    catch { return null; }
}
```

**Step 5: Fix all existing calls to UpdateTrayTooltip**

The old signature was `UpdateTrayTooltip(string? deviceName)`. The new signature is `UpdateTrayTooltip()` (no parameters — it reads state from the state machines). Update ALL call sites in the file:

- Constructor line 73: `UpdateTrayTooltip(null)` → `UpdateTrayTooltip()`
- Constructor line 98: `UpdateTrayTooltip(savedName)` → `UpdateTrayTooltip()`
- Constructor line 104: `UpdateTrayTooltip(null)` → `UpdateTrayTooltip()`

(Other call sites in `DeviceMenuItem_Click`, `ClearSelection_Click`, `OnDefaultDeviceChanged` have already been updated in their respective replacements above.)

**Step 6: Update Dispose to handle tuple Tag**

In the `Dispose` method (line 425-429) and the cleanup at the top of `RefreshDeviceList`, the `Tag` check needs to handle the new tuple format. Update the Tag pattern in both locations from:

```csharp
if (item is ToolStripMenuItem { Tag: MMDevice d })
```

To:

```csharp
if (item is ToolStripMenuItem mi)
{
    if (mi.Tag is (MMDevice d, DataFlow _))
        d.Dispose();
    else if (mi.Tag is MMDevice d2)
        d2.Dispose();
}
```

Note: The `RefreshDeviceList` cleanup at the top has already been rewritten in Step 1 — just ensure the `Dispose` method is updated.

**Step 7: Build and run tests**

Run: `dotnet build AudioLeash.sln && dotnet test AudioLeash.sln`
Expected: BUILD SUCCEEDED, ALL PASS

**Step 8: Commit**

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "feat: add recording device section to tray menu

Menu now shows Playback and Recording sections with bold headers.
Device clicks route to the correct state machine based on DataFlow.
Clear Selection clears both playback and recording selections.
Tooltip shows both locked device names."
```

---

### Task 5: Update Constructor Startup Logic for New Tooltip

**Files:**
- Modify: `AudioLeash/AudioLeashContext.cs`

The constructor (lines 68-113) currently calls `UpdateTrayTooltip(savedName)` and `UpdateTrayTooltip(null)`. Since `UpdateTrayTooltip()` now reads from state machines, the constructor logic simplifies:

**Step 1: Simplify the constructor startup block**

The playback restore block should no longer call `UpdateTrayTooltip` with a parameter. After both the playback and capture restore blocks run, call `UpdateTrayTooltip()` once. The welcome balloon and "device not found" balloon logic remain unchanged.

Replace the constructor block from line 68 to line 113 with:

```csharp
_settingsService = new SettingsService();
_startupService  = new StartupService();

// Restore playback device selection.
string? savedPlaybackId = _settingsService.LoadSelectedPlaybackDeviceId();
bool showWelcome = false;
bool playbackNotFound = false;

if (savedPlaybackId is null)
{
    if (!_settingsService.HasSettingsFile)
        showWelcome = true;
}
else
{
    var active = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
    bool available = active.Any(d => d.ID == savedPlaybackId);
    foreach (var d in active) d.Dispose();

    if (available)
    {
        _selection.SelectDevice(savedPlaybackId);
    }
    else
    {
        _settingsService.SaveSelectedPlaybackDeviceId(null);
        playbackNotFound = true;
    }
}

// Restore capture (recording) device selection.
string? savedCaptureId = _settingsService.LoadSelectedCaptureDeviceId();
if (savedCaptureId is not null)
{
    var activeCaptureDevices = _enumerator
        .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
    bool captureAvailable = activeCaptureDevices.Any(d => d.ID == savedCaptureId);
    foreach (var d in activeCaptureDevices) d.Dispose();

    if (captureAvailable)
    {
        _captureSelection.SelectDevice(savedCaptureId);
    }
    else
    {
        _settingsService.SaveSelectedCaptureDeviceId(null);
    }
}

UpdateTrayTooltip();

if (showWelcome)
{
    _trayIcon.ShowBalloonTip(
        timeout:  4000,
        tipTitle: "Welcome to AudioLeash",
        tipText:  "Click the tray icon and select a device to enable auto-restore.",
        tipIcon:  ToolTipIcon.Info);
}
else if (playbackNotFound)
{
    _trayIcon.ShowBalloonTip(
        timeout:  4000,
        tipTitle: "Saved Device Not Found",
        tipText:  "Your saved audio device was not found. Select a device from the tray menu to re-enable auto-restore.",
        tipIcon:  ToolTipIcon.Info);
}
```

Note: This task replaces the capture restore block added in Task 3 Step 2, consolidating both restore paths into a single clean flow.

**Step 2: Build and run tests**

Run: `dotnet build AudioLeash.sln && dotnet test AudioLeash.sln`
Expected: BUILD SUCCEEDED, ALL PASS

**Step 3: Commit**

```bash
git add AudioLeash/AudioLeashContext.cs
git commit -m "refactor: consolidate startup restore for playback and capture

Simplifies constructor to restore both playback and capture selections
in a clean flow, calling UpdateTrayTooltip() once after both are resolved."
```

---

### Task 6: Update README.md

**Files:**
- Modify: `README.md`

**Step 1: Update the README**

1. In the "Current Feature Set" section, update section "2. Audio Device Listing" to mention both playback and recording devices.

2. Add a new section after the existing features:

```markdown
### 14. Recording Device Support
- The tray menu shows two sections: **Playback** devices and **Recording** (microphone) devices, each with a bold section header.
- Users can independently lock a playback device and a recording device.
- When Windows changes either default device (e.g. due to a USB mic being plugged in), AudioLeash detects the change and restores the user's chosen device.
- **Clear Selection** resets both playback and recording selections.
- The tray tooltip shows both locked device names.
- Settings are persisted independently; existing settings from older versions are migrated automatically.
```

3. In "Ideas for Future Development", change the "Recording device support" line to:
```markdown
- ~~**Recording device support**~~ — ✔ Implemented (tray menu shows separate Playback and Recording sections; each can be locked independently).
```

4. Update the project description at the top (line 3) from mentioning just "audio output" to also cover "audio input":
```markdown
Keeps Windows on a leash — a lightweight system tray app that stops Windows from switching your audio output or input without permission, and snaps it back when it tries.
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: document recording device support in README"
```

---

### Task 7: Final Build, Test, and Review

**Step 1: Clean build**

Run: `dotnet build AudioLeash.sln -c Release`
Expected: BUILD SUCCEEDED with no warnings.

**Step 2: Run all tests**

Run: `dotnet test AudioLeash.sln --logger "console;verbosity=detailed"`
Expected: ALL PASS

**Step 3: Review changes**

Run: `git diff main --stat` and `git log --oneline main..HEAD`

Review the full diff for:
- No hardcoded `DataFlow.Render` remaining in notification handling
- All `UpdateTrayTooltip` calls use the new parameterless signature
- `Dispose` handles both tag formats
- Settings migration works for old format
- Menu item Tags use the new `(MMDevice, DataFlow)` tuple

**Step 4: Request code review via superpowers skill**

Use superpowers:requesting-code-review to validate the implementation.
