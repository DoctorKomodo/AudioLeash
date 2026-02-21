# Migrate off AudioSwitcher Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the unmaintained `AudioSwitcher.AudioApi.CoreAudio` package (and its `System.Reactive` dependency) with `NAudio.Wasapi` for device enumeration/events and a self-contained `PolicyConfigClient.cs` for COM-based default-device switching — preserving all existing behaviour with no regressions.

**Architecture:** `MMDeviceEnumerator` replaces `CoreAudioController` for enumeration and default-device queries. `IMMNotificationClient` (implemented as a nested private class inside `AudioLeashContext`) replaces the Rx observable for change events. `PolicyConfigClient` (a standalone COM-interop file) replaces `device.SetAsDefaultAsync()`. A small `DeviceSelectionState` class is extracted from `AudioSwitcherContext` to make the core state machine unit-testable.

**Tech Stack:** C# 12 / .NET 8 / WinForms · NAudio.Wasapi 2.2.1 · COM interop (no extra package) · xUnit + NSubstitute for tests

---

## Mapping: AudioSwitcher → NAudio

| Old (AudioSwitcher) | New (NAudio) |
|---|---|
| `CoreAudioController` | `MMDeviceEnumerator` + `PolicyConfigClient` |
| `IDevice.Id` (`Guid`) | `MMDevice.ID` (`string`) |
| `IDevice.FullName` | `MMDevice.FriendlyName` |
| `DeviceState.Active` | `DeviceState.Active` (same name) |
| `controller.DefaultPlaybackDevice` | `enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)` |
| `controller.GetPlaybackDevices()` | `enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)` |
| `controller.AudioDeviceChanged` (Rx) | `IMMNotificationClient.OnDefaultDeviceChanged` |
| `device.SetAsDefaultAsync()` | `policyConfigClient.SetDefaultEndpoint(device.ID)` |
| `selectedDeviceId` (`Guid?`) | `selectedDeviceId` (`string?`) |

---

## Task 1: Create feature branch and swap NuGet packages

**Files:**
- Modify: `AudioLeash/AudioLeash.csproj`

**Step 1: Create the feature branch**

```bash
git checkout -b feature/migrate-off-audioswitcher
```

Expected: `Switched to a new branch 'feature/migrate-off-audioswitcher'`

**Step 2: Replace packages in the csproj**

In `AudioLeash/AudioLeash.csproj`, replace the `<ItemGroup>` containing `PackageReference` entries with:

```xml
<ItemGroup>
  <PackageReference Include="NAudio.Wasapi" Version="2.2.1" />
</ItemGroup>
```

Remove the `AudioSwitcher.AudioApi.CoreAudio` and `System.Reactive` package references entirely.

**Step 3: Restore packages**

```bash
dotnet restore AudioLeash.sln
```

Expected: Restored successfully, no NU1701 warnings.

**Step 4: Verify build fails with expected errors**

```bash
dotnet build AudioLeash.sln 2>&1 | head -30
```

Expected: Build errors referencing `AudioSwitcher`, `CoreAudioController`, `DeviceChangedArgs`, etc.
This is correct — the old types no longer exist. Proceed.

**Step 5: Commit**

```bash
git add AudioLeash/AudioLeash.csproj
git commit -m "chore: swap AudioSwitcher for NAudio.Wasapi, drop System.Reactive"
```

---

## Task 2: Create the test project

**Files:**
- Create: `AudioLeash.Tests/AudioLeash.Tests.csproj`

**Step 1: Create the xUnit test project**

```bash
dotnet new xunit -n AudioLeash.Tests -o AudioLeash.Tests
dotnet sln AudioLeash.sln add AudioLeash.Tests/AudioLeash.Tests.csproj
dotnet add AudioLeash.Tests/AudioLeash.Tests.csproj reference AudioLeash/AudioLeash.csproj
dotnet add AudioLeash.Tests/AudioLeash.Tests.csproj package NSubstitute
```

**Step 2: Delete the placeholder test file**

Delete `AudioLeash.Tests/UnitTest1.cs`.

**Step 3: Verify project builds**

```bash
dotnet build AudioLeash.Tests/AudioLeash.Tests.csproj
```

Expected: Build succeeds (the test project has no compile errors even though the main project does).

**Step 4: Commit**

```bash
git add AudioLeash.Tests/ AudioLeash.sln
git commit -m "test: add xUnit test project with NSubstitute"
```

---

## Task 3: Extract DeviceSelectionState (TDD)

This class holds the three pieces of pure business logic that are currently embedded in `AudioSwitcherContext`:
1. Which device the user has selected
2. The internal-change guard flag
3. The decision of whether to restore, clear, or do nothing when Windows changes the default

**Files:**
- Create: `AudioLeash/DeviceSelectionState.cs`
- Create: `AudioLeash.Tests/DeviceSelectionStateTests.cs`

**Step 1: Write the failing tests**

Create `AudioLeash.Tests/DeviceSelectionStateTests.cs`:

```csharp
using AudioLeash;
using Xunit;

namespace AudioLeash.Tests;

public class DeviceSelectionStateTests
{
    // ── EvaluateDefaultChange ────────────────────────────────────────────

    [Fact]
    public void EvaluateDefaultChange_WhenNoDeviceSelected_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-A",
            isSelectedDeviceAvailable: true);
        Assert.Equal(RestoreDecision.NoAction, result);
    }

    [Fact]
    public void EvaluateDefaultChange_WhenIsInternalChange_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.IsInternalChange = true;

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-B",
            isSelectedDeviceAvailable: true);

        Assert.Equal(RestoreDecision.NoAction, result);
    }

    [Fact]
    public void EvaluateDefaultChange_WhenNewDefaultMatchesSelected_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-A",
            isSelectedDeviceAvailable: true);

        Assert.Equal(RestoreDecision.NoAction, result);
    }

    [Fact]
    public void EvaluateDefaultChange_WhenExternalChangeAndDeviceAvailable_ReturnsRestore()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-B",
            isSelectedDeviceAvailable: true);

        Assert.Equal(RestoreDecision.Restore, result);
    }

    [Fact]
    public void EvaluateDefaultChange_WhenSelectedDeviceUnavailable_ReturnsClearSelection()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-B",
            isSelectedDeviceAvailable: false);

        Assert.Equal(RestoreDecision.ClearSelection, result);
    }

    // ── SelectDevice / ClearSelection ────────────────────────────────────

    [Fact]
    public void SelectDevice_StoresProvidedId()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        Assert.Equal("device-A", state.SelectedDeviceId);
    }

    [Fact]
    public void ClearSelection_NullsTheStoredId()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.ClearSelection();
        Assert.Null(state.SelectedDeviceId);
    }

    // ── IsInternalChange flag ────────────────────────────────────────────

    [Fact]
    public void IsInternalChange_DefaultsToFalse()
    {
        var state = new DeviceSelectionState();
        Assert.False(state.IsInternalChange);
    }
}
```

**Step 2: Run tests to confirm they fail**

```bash
dotnet test AudioLeash.Tests/AudioLeash.Tests.csproj --logger "console;verbosity=normal" 2>&1
```

Expected: compile error — `DeviceSelectionState` and `RestoreDecision` do not exist yet.

**Step 3: Implement DeviceSelectionState**

Create `AudioLeash/DeviceSelectionState.cs`:

```csharp
namespace AudioLeash;

internal enum RestoreDecision
{
    NoAction,
    Restore,
    ClearSelection,
}

/// <summary>
/// Pure state machine for the device selection and auto-restore logic.
/// Contains no WinForms or audio-stack dependencies — fully unit-testable.
/// </summary>
internal sealed class DeviceSelectionState
{
    /// <summary>ID of the device the user has explicitly selected.</summary>
    public string? SelectedDeviceId { get; private set; }

    /// <summary>
    /// Set to <c>true</c> while the app itself is switching the default device,
    /// to prevent the change-notification handler from triggering a feedback loop.
    /// </summary>
    public bool IsInternalChange { get; set; }

    public void SelectDevice(string deviceId) => SelectedDeviceId = deviceId;

    public void ClearSelection() => SelectedDeviceId = null;

    /// <summary>
    /// Decides what the app should do when Windows reports that the default
    /// playback device has changed to <paramref name="newDefaultId"/>.
    /// </summary>
    /// <param name="newDefaultId">The ID of the device Windows just made the default.</param>
    /// <param name="isSelectedDeviceAvailable">
    ///   Whether the user-selected device is currently present and active.
    /// </param>
    public RestoreDecision EvaluateDefaultChange(
        string newDefaultId,
        bool isSelectedDeviceAvailable)
    {
        if (IsInternalChange)          return RestoreDecision.NoAction;
        if (SelectedDeviceId is null)  return RestoreDecision.NoAction;
        if (newDefaultId == SelectedDeviceId) return RestoreDecision.NoAction;
        if (!isSelectedDeviceAvailable) return RestoreDecision.ClearSelection;

        return RestoreDecision.Restore;
    }
}
```

**Step 4: Run tests to confirm they pass**

```bash
dotnet test AudioLeash.Tests/AudioLeash.Tests.csproj --logger "console;verbosity=normal" 2>&1
```

Expected: All 9 tests PASS.

**Step 5: Commit**

```bash
git add AudioLeash/DeviceSelectionState.cs AudioLeash.Tests/DeviceSelectionStateTests.cs
git commit -m "feat: extract DeviceSelectionState with full unit test coverage"
```

---

## Task 4: Add PolicyConfigClient.cs

This file contains the COM interop required to call the undocumented Windows `IPolicyConfig` interface, which is the only documented-by-reverse-engineering way to programmatically set the default audio endpoint. No unit tests are written for this file — it is thin COM glue and requires a live Windows audio stack to test.

**Files:**
- Create: `AudioLeash/PolicyConfigClient.cs`

**Step 1: Create the file**

Create `AudioLeash/PolicyConfigClient.cs`:

```csharp
#nullable enable
using System;
using System.Runtime.InteropServices;

namespace AudioLeash;

// Mirrors the ERole enum from mmdeviceapi.h.
// Must match the integer values Windows expects in the COM vtable call.
internal enum ERole
{
    Console        = 0,
    Multimedia     = 1,
    Communications = 2,
}

// ── COM interface: Win7+ ─────────────────────────────────────────────────────
// Method ordering must exactly match the COM vtable layout.
// Methods we never call are stubs that keep the vtable offsets correct.
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat(      [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr ppFormat);
    [PreserveSig] int ResetDeviceFormat(    [MarshalAs(UnmanagedType.LPWStr)] string dev);
    [PreserveSig] int SetDeviceFormat(      [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pEndpointFormat, IntPtr pMixFormat);
    [PreserveSig] int GetProcessingPeriod(  [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr pmftDefault, IntPtr pmftMin);
    [PreserveSig] int SetProcessingPeriod(  [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pmftPeriod);
    [PreserveSig] int GetShareMode(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int SetShareMode(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int GetPropertyValue(     [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFxStore, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetPropertyValue(     [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFxStore, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint(   [MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bVisible);
}

// ── COM interface: Vista-era fallback ────────────────────────────────────────
[Guid("568b9108-44bf-40b4-9006-86afe5b5a620")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigVista
{
    [PreserveSig] int GetMixFormat(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat(      [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr ppFormat);
    [PreserveSig] int SetDeviceFormat(      [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pEndpointFormat, IntPtr pMixFormat);
    [PreserveSig] int GetProcessingPeriod(  [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr pmftDefault, IntPtr pmftMin);
    [PreserveSig] int SetProcessingPeriod(  [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pmftPeriod);
    [PreserveSig] int GetShareMode(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int SetShareMode(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int GetPropertyValue(     [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFxStore, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetPropertyValue(     [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFxStore, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint(   [MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bVisible);
}

// ── COM-creatable class (CLSID works on Win7–Win11) ──────────────────────────
[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class CPolicyConfigClient { }

// ── Public wrapper ───────────────────────────────────────────────────────────

/// <summary>
/// Sets the Windows default audio playback endpoint via the undocumented
/// <c>IPolicyConfig</c> COM interface (reverse-engineered; stable since Vista).
/// </summary>
internal sealed class PolicyConfigClient
{
    private readonly IPolicyConfig?      _v7;
    private readonly IPolicyConfigVista? _vista;

    public PolicyConfigClient()
    {
        var com = new CPolicyConfigClient();
        _v7   = com as IPolicyConfig;
        _vista = _v7 is null ? com as IPolicyConfigVista : null;
    }

    /// <summary>
    /// Sets <paramref name="deviceId"/> as the Windows default playback device
    /// for all three roles (Console, Multimedia, Communications), mirroring
    /// what the Windows sound control panel does.
    /// </summary>
    /// <param name="deviceId">
    /// The <c>MMDevice.ID</c> string, e.g. <c>{0.0.0.00000000}.{guid}</c>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when neither COM interface is available (audio stack unavailable).
    /// </exception>
    public void SetDefaultEndpoint(string deviceId)
    {
        if (_v7 is not null)
        {
            Marshal.ThrowExceptionForHR(_v7.SetDefaultEndpoint(deviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(_v7.SetDefaultEndpoint(deviceId, ERole.Multimedia));
            Marshal.ThrowExceptionForHR(_v7.SetDefaultEndpoint(deviceId, ERole.Communications));
            return;
        }

        if (_vista is not null)
        {
            Marshal.ThrowExceptionForHR(_vista.SetDefaultEndpoint(deviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(_vista.SetDefaultEndpoint(deviceId, ERole.Multimedia));
            Marshal.ThrowExceptionForHR(_vista.SetDefaultEndpoint(deviceId, ERole.Communications));
            return;
        }

        throw new InvalidOperationException(
            "Could not obtain IPolicyConfig COM interface. " +
            "The Windows audio stack may be unavailable.");
    }
}
```

**Step 2: Verify file compiles**

```bash
dotnet build AudioLeash/AudioLeash.csproj 2>&1 | grep -E "(PolicyConfigClient|error)" | head -20
```

Expected: Errors only from the still-broken `AudioSwitcherContext.cs`, not from `PolicyConfigClient.cs`.

**Step 3: Commit**

```bash
git add AudioLeash/PolicyConfigClient.cs
git commit -m "feat: add PolicyConfigClient COM interop for SetDefaultEndpoint"
```

---

## Task 5: Rewrite AudioSwitcherContext.cs → AudioLeashContext.cs

Replace the entire `AudioSwitcherContext.cs` file with a NAudio-based implementation. The class is renamed `AudioLeashContext` since it no longer has anything to do with AudioSwitcher. `IMMNotificationClient` is implemented as a private nested class to keep all audio-event logic in one file while satisfying the GC-lifetime requirement (the instance is held as a field).

**Files:**
- Modify: `AudioLeash/AudioSwitcherContext.cs` (rename + full rewrite)
- Modify: `AudioLeash/Program.cs` (update class name reference)

**Step 1: Rewrite AudioSwitcherContext.cs**

Replace the entire contents of `AudioLeash/AudioSwitcherContext.cs` with:

```csharp
#nullable enable
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioLeash;

/// <summary>
/// Application context that manages the system tray icon and audio device switching.
/// Runs without a visible window — entirely through the notification area.
/// </summary>
public sealed class AudioLeashContext : ApplicationContext
{
    // ─── Fields ───────────────────────────────────────────────────────────────

    private readonly NotifyIcon          _trayIcon;
    private readonly ContextMenuStrip    _contextMenu;
    private readonly MMDeviceEnumerator  _enumerator;
    private readonly PolicyConfigClient  _policyConfig;
    private readonly DeviceSelectionState _selection;
    private readonly AudioNotificationClient _notificationClient; // must be a field — see GC note below

    // ─── Constructor ──────────────────────────────────────────────────────────

    public AudioLeashContext()
    {
        _enumerator     = new MMDeviceEnumerator();
        _policyConfig   = new PolicyConfigClient();
        _selection      = new DeviceSelectionState();
        _contextMenu    = new ContextMenuStrip();

        _trayIcon = new NotifyIcon
        {
            Icon             = GetTrayIcon(),
            ContextMenuStrip = _contextMenu,
            Text             = "AudioLeash",
            Visible          = true,
        };
        _trayIcon.MouseClick += TrayIcon_MouseClick;

        // Register for device-change notifications.
        // IMPORTANT: _notificationClient must be a class field, not a local variable.
        // If the GC collects it while Windows holds a native pointer to it the process
        // will crash with ExecutionEngineException. (NAudio issue #849)
        _notificationClient = new AudioNotificationClient(OnDefaultDeviceChanged);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);

        // Seed the selection with whatever Windows currently uses as the default.
        using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (defaultDevice is not null)
            _selection.SelectDevice(defaultDevice.ID);

        RefreshDeviceList();
    }

    // ─── Tray Icon Events ─────────────────────────────────────────────────────

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        RefreshDeviceList();

        // WinForms only shows the context menu automatically on right-click;
        // invoke it manually for left-click via reflection.
        typeof(NotifyIcon)
            .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(_trayIcon, null);
    }

    // ─── Device List ──────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the context menu with the current list of active playback devices.
    /// Called on every left-click and after any device switch.
    /// </summary>
    private void RefreshDeviceList()
    {
        // Dispose MMDevice objects from the previous menu build to release COM references.
        foreach (ToolStripItem item in _contextMenu.Items)
        {
            if (item is ToolStripMenuItem { Tag: MMDevice d })
                d.Dispose();
        }
        _contextMenu.Items.Clear();

        var devices = _enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .OrderBy(d => d.FriendlyName)
            .ToList();

        using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        string? defaultId = defaultDevice?.ID;

        if (devices.Count == 0)
        {
            _contextMenu.Items.Add(new ToolStripMenuItem("No devices available") { Enabled = false });
        }
        else
        {
            foreach (var device in devices)
            {
                bool isUserSelected   = _selection.SelectedDeviceId == device.ID;
                bool isWindowsDefault = defaultId == device.ID;

                string label = device.FriendlyName;
                if (isWindowsDefault && !isUserSelected)
                    label += "  (Windows default)";

                var menuItem = new ToolStripMenuItem(label)
                {
                    Tag     = device,
                    Checked = isUserSelected,
                };
                menuItem.Click += DeviceMenuItem_Click;
                _contextMenu.Items.Add(menuItem);
            }
        }

        _contextMenu.Items.Add(new ToolStripSeparator());

        var clearItem = new ToolStripMenuItem("Clear Selection  (disable auto-restore)")
        {
            Enabled = _selection.SelectedDeviceId is not null,
        };
        clearItem.Click += ClearSelection_Click;
        _contextMenu.Items.Add(clearItem);

        var refreshItem = new ToolStripMenuItem("Refresh List");
        refreshItem.Click += (_, _) => RefreshDeviceList();
        _contextMenu.Items.Add(refreshItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += Exit_Click;
        _contextMenu.Items.Add(exitItem);
    }

    // ─── Device Selection ─────────────────────────────────────────────────────

    private async void DeviceMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: MMDevice device })
            return;

        try
        {
            _selection.IsInternalChange = true;

            // Run COM call off the UI thread to avoid blocking the message pump.
            string deviceId   = device.ID;
            string deviceName = device.FriendlyName;
            await System.Threading.Tasks.Task.Run(() => _policyConfig.SetDefaultEndpoint(deviceId));

            _selection.SelectDevice(deviceId);

            _trayIcon.ShowBalloonTip(
                timeout:  2500,
                tipTitle: "Audio Device Selected",
                tipText:  $"Default output: {deviceName}\n\nAuto-restore is now active.",
                tipIcon:  ToolTipIcon.Info);

            RefreshDeviceList();
        }
        catch (Exception ex)
        {
            ShowError($"Could not set device:\n{ex.Message}");
        }
        finally
        {
            _selection.IsInternalChange = false;
        }
    }

    private void ClearSelection_Click(object? sender, EventArgs e)
    {
        _selection.ClearSelection();

        _trayIcon.ShowBalloonTip(
            timeout:  2500,
            tipTitle: "Selection Cleared",
            tipText:  "Auto-restore disabled. Select a device to re-enable.",
            tipIcon:  ToolTipIcon.Info);

        RefreshDeviceList();
    }

    // ─── Device Change Monitoring ─────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="AudioNotificationClient"/> on a Windows audio thread
    /// when the system default playback device changes.
    /// </summary>
    private void OnDefaultDeviceChanged(string newDefaultId)
    {
        bool isSelectedAvailable = _selection.SelectedDeviceId is not null
            && _enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Any(d => d.ID == _selection.SelectedDeviceId);

        var decision = _selection.EvaluateDefaultChange(newDefaultId, isSelectedAvailable);

        switch (decision)
        {
            case RestoreDecision.NoAction:
                return;

            case RestoreDecision.ClearSelection:
                _selection.ClearSelection();
                SafeInvoke(() =>
                {
                    _trayIcon.ShowBalloonTip(
                        timeout:  3000,
                        tipTitle: "Audio Device Unavailable",
                        tipText:  "Your selected device is no longer available. Selection cleared.",
                        tipIcon:  ToolTipIcon.Warning);
                    RefreshDeviceList();
                });
                break;

            case RestoreDecision.Restore:
                try
                {
                    _selection.IsInternalChange = true;
                    string restoreId = _selection.SelectedDeviceId!;
                    _policyConfig.SetDefaultEndpoint(restoreId);

                    string restoredName = _enumerator
                        .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                        .FirstOrDefault(d => d.ID == restoreId)
                        ?.FriendlyName ?? restoreId;

                    SafeInvoke(() =>
                    {
                        _trayIcon.ShowBalloonTip(
                            timeout:  3000,
                            tipTitle: "Audio Device Restored",
                            tipText:  $"Switched back to: {restoredName}",
                            tipIcon:  ToolTipIcon.Info);
                        RefreshDeviceList();
                    });
                }
                catch (Exception ex)
                {
                    SafeInvoke(() =>
                    {
                        _trayIcon.ShowBalloonTip(
                            timeout:  3000,
                            tipTitle: "Restore Failed",
                            tipText:  $"Could not restore audio device:\n{ex.Message}",
                            tipIcon:  ToolTipIcon.Error);
                    });
                }
                finally
                {
                    _selection.IsInternalChange = false;
                }
                break;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SafeInvoke(Action action)
    {
        if (_contextMenu.InvokeRequired)
            _contextMenu.Invoke(action);
        else
            action();
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "AudioLeash", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static Icon GetTrayIcon()
    {
        try
        {
            string iconPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Resources", "icon.ico");
            if (System.IO.File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { /* fall through to default */ }
        return SystemIcons.Application;
    }

    // ─── Exit ─────────────────────────────────────────────────────────────────

    private void Exit_Click(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);

            // Dispose MMDevice objects stored in menu Tags.
            foreach (ToolStripItem item in _contextMenu.Items)
            {
                if (item is ToolStripMenuItem { Tag: MMDevice d })
                    d.Dispose();
            }

            _trayIcon.Dispose();
            _contextMenu.Dispose();
            _enumerator.Dispose();
        }
        base.Dispose(disposing);
    }

    // ─── IMMNotificationClient (nested) ───────────────────────────────────────

    /// <summary>
    /// Receives Windows audio endpoint change notifications.
    /// Must be kept alive as a class field — not a local variable — to prevent
    /// the GC from collecting it while Windows holds a native COM pointer to it.
    /// </summary>
    private sealed class AudioNotificationClient : IMMNotificationClient
    {
        private readonly Action<string> _onDefaultChanged;

        internal AudioNotificationClient(Action<string> onDefaultChanged)
            => _onDefaultChanged = onDefaultChanged;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Only react to playback default changing; ignore duplicates for
            // Console/Communications roles and ignore capture devices.
            if (flow == DataFlow.Render && role == Role.Multimedia)
                _onDefaultChanged(defaultDeviceId);
        }

        // These are required by the interface but AudioLeash does not act on them.
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
```

**Step 2: Update Program.cs**

Read `AudioLeash/Program.cs`. It will reference `AudioSwitcherContext`. Change the reference to `AudioLeashContext`.

The typical line looks like:
```csharp
Application.Run(new AudioSwitcherContext());
```

Change it to:
```csharp
Application.Run(new AudioLeashContext());
```

**Step 3: Build and verify no errors**

```bash
dotnet build AudioLeash.sln 2>&1
```

Expected: Build succeeds with 0 errors. There may be a warning about the file still being named `AudioSwitcherContext.cs` — that is fine; the filename does not have to match the class name in C#.

**Step 4: Run tests to confirm the state-machine tests still pass**

```bash
dotnet test AudioLeash.sln --logger "console;verbosity=normal" 2>&1
```

Expected: All 9 tests PASS.

**Step 5: Commit**

```bash
git add AudioLeash/AudioSwitcherContext.cs AudioLeash/Program.cs
git commit -m "feat: migrate AudioLeashContext to NAudio.Wasapi + PolicyConfigClient, drop AudioSwitcher"
```

---

## Task 6: Update README

**Files:**
- Modify: `README.md`

**Step 1: Update the NuGet dependencies table**

Find the table:
```markdown
| `AudioSwitcher.AudioApi.CoreAudio` 3.1.0 | Device enumeration, default-device switching, change events |
```

Replace with:
```markdown
| `NAudio.Wasapi` 2.2.1 | Device enumeration, change events (IMMNotificationClient) |
```

**Step 2: Update the Project Structure section**

Find the file listing in the Project Structure code block and update `AudioSwitcherContext.cs` references:

```
├── Program.cs               ← Entry point; runs AudioLeashContext
├── AudioSwitcherContext.cs  ← All application logic
├── DeviceSelectionState.cs  ← Pure selection state machine (unit-testable)
└── PolicyConfigClient.cs    ← COM interop: sets Windows default audio endpoint
```

**Step 3: Update the Future Development section**

Remove the "Migrate off AudioSwitcher" bullet point from the Ideas for Future Development list — it is now complete.

**Step 4: Commit**

```bash
git add README.md
git commit -m "docs: update README for NAudio migration (packages, structure, future ideas)"
```

---

## Task 7: Final verification

**Step 1: Full clean build**

```bash
dotnet build AudioLeash.sln -c Release 2>&1
```

Expected: Build succeeds, 0 errors, 0 warnings (no more NU1701 compatibility warning).

**Step 2: Full test run**

```bash
dotnet test AudioLeash.sln --logger "console;verbosity=detailed" 2>&1
```

Expected: All tests PASS.

**Step 3: Verify the old dependency is gone**

```bash
dotnet list AudioLeash/AudioLeash.csproj package 2>&1
```

Expected: Only `NAudio.Wasapi` listed. No `AudioSwitcher` or `System.Reactive`.

**Step 4: Request code review**

Follow the project's code review workflow:
1. Big-picture review subagent (how the new implementation is used, implications)
2. Standard review subagent (code quality, correctness, edge cases)

Address any findings before merging.

**Step 5: Merge to main**

Only after both reviews pass and the user approves:

```bash
git checkout main
git merge --no-ff feature/migrate-off-audioswitcher -m "feat: migrate off AudioSwitcher to NAudio.Wasapi + PolicyConfigClient"
git push
git branch -d feature/migrate-off-audioswitcher
```
