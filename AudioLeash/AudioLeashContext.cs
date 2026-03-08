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
/// Runs without a visible window entirely through the notification area.
/// </summary>
public sealed class AudioLeashContext : ApplicationContext
{
    private readonly NotifyIcon              _trayIcon;
    private readonly ContextMenuStrip        _contextMenu;
    private readonly MMDeviceEnumerator      _enumerator;
    private readonly PolicyConfigClient      _policyConfig;
    private readonly DeviceSelectionState    _selection;
    private readonly DeviceSelectionState    _captureSelection;
    private readonly AudioNotificationClient _notificationClient;
    private readonly SettingsService         _settingsService;
    private readonly StartupService          _startupService;
    private readonly Font                    _sectionHeaderFont;

    public AudioLeashContext()
    {
        _enumerator   = new MMDeviceEnumerator();
        _policyConfig = new PolicyConfigClient();
        _selection        = new DeviceSelectionState();
        _captureSelection = new DeviceSelectionState();
        _contextMenu      = new ContextMenuStrip();
        _sectionHeaderFont = new Font(_contextMenu.Font, FontStyle.Bold);
        ApplyTheme();

        _trayIcon = new NotifyIcon
        {
            Icon             = GetTrayIcon(),
            ContextMenuStrip = _contextMenu,
            Text             = "AudioLeash",
            Visible          = true,
        };
        _trayIcon.MouseClick += TrayIcon_MouseClick;

        // Force the ContextMenuStrip's HWND to be created now, before registering for
        // device-change notifications. Control.InvokeRequired returns false when
        // IsHandleCreated is false (regardless of which thread is calling), so SafeInvoke
        // would incorrectly run on the audio COM thread before the menu is first displayed.
        // Accessing .Handle forces handle creation and must happen before calling
        // RegisterEndpointNotificationCallback to guarantee "handle exists before any
        // notification can arrive".
        _ = _contextMenu.Handle;
        _contextMenu.Opening += (_, _) => RefreshDeviceList();

        // Subscribe to Windows theme changes so the menu renderer updates in real time
        // when the user toggles dark/light mode in Windows Settings.
        WindowsTheme.Changed += OnThemeChanged;

        // Register for device-change notifications.
        // IMPORTANT: _notificationClient must be a class field, not a local variable.
        // If the GC collects it while Windows holds a native COM pointer the process
        // will crash with ExecutionEngineException. (NAudio issue #849)
        _notificationClient = new AudioNotificationClient(OnDefaultDeviceChanged, OnDeviceStateChanged);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);

        _settingsService = new SettingsService();
        _startupService  = new StartupService();

        // Restore saved device selections (playback and capture).
        string? savedPlaybackId = _settingsService.LoadSelectedPlaybackDeviceId();
        string? savedCaptureId  = _settingsService.LoadSelectedCaptureDeviceId();
        bool isFirstRun = savedPlaybackId is null && !_settingsService.HasSettingsFile;

        bool playbackRestored = TryRestoreSavedDevice(DataFlow.Render, _selection, savedPlaybackId);
        bool captureRestored  = TryRestoreSavedDevice(DataFlow.Capture, _captureSelection, savedCaptureId);
        bool playbackUnavailable = savedPlaybackId is not null && !playbackRestored;
        bool captureUnavailable  = savedCaptureId is not null && !captureRestored;

        UpdateTrayTooltip();

        if (isFirstRun)
        {
            _trayIcon.ShowBalloonTip(
                timeout:  4000,
                tipTitle: "Welcome to AudioLeash",
                tipText:  "Click the tray icon and select a device to enable auto-restore.",
                tipIcon:  ToolTipIcon.Info);
        }
        else if (playbackUnavailable || captureUnavailable)
        {
            string detail = (playbackUnavailable, captureUnavailable) switch
            {
                (true, true)   => "Your saved playback and recording devices are",
                (true, false)  => "Your saved playback device is",
                (false, true)  => "Your saved recording device is",
                _              => "Your saved device is", // unreachable
            };
            _trayIcon.ShowBalloonTip(
                timeout:  4000,
                tipTitle: "Saved Device Unavailable",
                tipText:  $"{detail} currently disconnected. AudioLeash will restore automatically when reconnected.",
                tipIcon:  ToolTipIcon.Info);
        }

        RefreshDeviceList();
    }

    /// <summary>
    /// Attempts to restore a previously saved device selection for the given flow.
    /// Always selects the device (even if currently unavailable).
    /// Returns <c>true</c> if the device is currently active, <c>false</c> if unavailable.
    /// </summary>
    private bool TryRestoreSavedDevice(DataFlow flow, DeviceSelectionState selection, string? savedId)
    {
        if (savedId is null) return false;

        var active = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
        bool available = active.Any(d => d.ID == savedId);
        foreach (var d in active) d.Dispose();

        selection.SelectDevice(savedId);
        selection.SetDeviceAvailability(available);
        return available;
    }

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // WinForms only shows the context menu automatically on right-click;
        // invoke it manually for left-click via reflection.
        typeof(NotifyIcon)
            .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(_trayIcon, null);
    }

    /// <summary>
    /// Rebuilds the context menu with the current list of active playback and recording devices.
    /// Called on every menu open and after any device switch.
    /// </summary>
    private void RefreshDeviceList()
    {
        foreach (ToolStripItem item in _contextMenu.Items)
        {
            if (item is ToolStripMenuItem mi)
            {
                if (mi.Tag is (MMDevice d, DataFlow _))
                    d.Dispose();
                else if (mi.Tag is MMDevice d2)
                    d2.Dispose();
            }
        }
        _contextMenu.Items.Clear();

        // ── Playback section ────────────────────────────────────────────
        AddDeviceSection(DataFlow.Render, "Playback", _selection);

        // ── Recording section ───────────────────────────────────────────
        AddDeviceSection(DataFlow.Capture, "Recording", _captureSelection);

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
            Font = _sectionHeaderFont,
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

        if (devices.Count == 0 && !(selection.SelectedDeviceId is not null && !selection.IsDeviceAvailable))
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

        // Show unavailable selected device as a grayed-out, checked entry
        if (selection.SelectedDeviceId is not null && !selection.IsDeviceAvailable)
        {
            string unavailableName = GetDeviceName(selection.SelectedDeviceId)
                                     ?? "Unknown Device";
            var unavailableItem = new ToolStripMenuItem($"{unavailableName}  (unavailable)")
            {
                Checked = true,
                Enabled = false,
            };
            _contextMenu.Items.Add(unavailableItem);
        }
    }

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

    private void StartupItem_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_startupService.IsEnabled)
                _startupService.Disable();
            else
                _startupService.Enable(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch (Exception ex)
        {
            ShowError($"Could not update startup setting:\n{ex.Message}");
        }

        RefreshDeviceList();
    }

    /// <summary>
    /// Called by <see cref="AudioNotificationClient"/> on a Windows audio thread
    /// when the system default device changes for either playback or capture.
    /// </summary>
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

            case RestoreDecision.Suspend:
                // Device is unavailable — keep the selection but suspend enforcement.
                // The OnDeviceStateChanged handler will restore when it reconnects.
                selection.SetDeviceAvailability(false);
                SafeInvoke(() =>
                {
                    UpdateTrayTooltip();
                    RefreshDeviceList();
                });
                break;

            case RestoreDecision.Restore:
                // Set the flag on the audio thread BEFORE dispatching to the UI thread.
                // This prevents a second OnDefaultDeviceChanged from triggering another
                // restore attempt during the roundtrip to the UI thread.
                selection.IsInternalChange = true;
                string restoreId = selection.SelectedDeviceId!;

                // CPolicyConfigClient is an STA COM object with no registered proxy/stub.
                // Calling it from the audio COM thread (a different apartment) causes an
                // InvalidCastException. Marshal the call to the UI STA thread instead.
                // SafeInvoke uses Control.Invoke (synchronous), so the audio thread
                // blocks here until the UI thread finishes and resets the flag.
                // The outer try/catch ensures the flag is cleared even if SafeInvoke
                // itself throws (e.g. ObjectDisposedException during shutdown).
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

    /// <summary>
    /// Called by <see cref="AudioNotificationClient"/> on a Windows audio thread
    /// when a device's state changes (connected, disconnected, etc.).
    /// </summary>
    private void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        bool isNowActive = newState == DeviceState.Active;

        ProcessDeviceStateChange(DataFlow.Render, _selection, deviceId, isNowActive);
        ProcessDeviceStateChange(DataFlow.Capture, _captureSelection, deviceId, isNowActive);
    }

    private void ProcessDeviceStateChange(
        DataFlow flow,
        DeviceSelectionState selection,
        string deviceId,
        bool isNowActive)
    {
        if (selection.SelectedDeviceId != deviceId) return;

        bool wasAvailable = selection.IsDeviceAvailable;
        var decision = selection.EvaluateDeviceStateChange(deviceId, isNowActive);
        string flowLabel = flow == DataFlow.Render ? "Audio Device" : "Recording Device";

        switch (decision)
        {
            case RestoreDecision.Restore:
                selection.IsInternalChange = true;
                try
                {
                    SafeInvoke(() =>
                    {
                        try
                        {
                            _policyConfig.SetDefaultEndpoint(deviceId);

                            string? deviceName = GetDeviceName(deviceId);
                            string displayName = deviceName ?? deviceId;

                            UpdateTrayTooltip();

                            _trayIcon.ShowBalloonTip(
                                timeout:  3000,
                                tipTitle: $"{flowLabel} Reconnected",
                                tipText:  $"Switched back to: {displayName}",
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

            case RestoreDecision.NoAction:
                // Device just became unavailable — notify and update UI
                if (wasAvailable && !isNowActive)
                {
                    SafeInvoke(() =>
                    {
                        string? deviceName = GetDeviceName(deviceId);
                        string displayName = deviceName ?? flowLabel.ToLower();

                        UpdateTrayTooltip();

                        _trayIcon.ShowBalloonTip(
                            timeout:  3000,
                            tipTitle: $"{flowLabel} Disconnected",
                            tipText:  $"{displayName} is no longer available. Waiting for reconnection…",
                            tipIcon:  ToolTipIcon.Warning);
                        RefreshDeviceList();
                    });
                }
                break;
        }
    }

    private void SafeInvoke(Action action)
    {
        if (!_contextMenu.IsHandleCreated) return;
        try
        {
            if (_contextMenu.InvokeRequired)
                _contextMenu.Invoke(action);
            else
                action(); // Already on the UI thread; invoke directly.
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (InvalidOperationException) { /* handle destroyed mid-flight */ }
    }

    /// <summary>
    /// Applies a renderer to <see cref="_contextMenu"/> that matches the current Windows
    /// colour theme (dark or light).  Safe to call on any thread that owns the menu handle.
    /// </summary>
    private void ApplyTheme() =>
        _contextMenu.Renderer = WindowsTheme.IsDarkMode
            ? new DarkMenuRenderer()
            : new ToolStripProfessionalRenderer();

    private void OnThemeChanged(object? sender, EventArgs e) => SafeInvoke(ApplyTheme);

    private static void ShowError(string message) =>
        MessageBox.Show(message, "AudioLeash", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void UpdateTrayTooltip()
    {
        string? playbackName = FormatDeviceTooltip(_selection);
        string? captureName  = FormatDeviceTooltip(_captureSelection);

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

    private string? FormatDeviceTooltip(DeviceSelectionState selection)
    {
        if (selection.SelectedDeviceId is null) return null;
        string? name = GetDeviceName(selection.SelectedDeviceId);
        if (name is null) return "Unknown (waiting)";
        return selection.IsDeviceAvailable ? name : $"{name} (waiting)";
    }

    private string? GetDeviceName(string? deviceId)
    {
        if (deviceId is null) return null;
        try
        {
            using var device = _enumerator.GetDevice(deviceId);
            return device.FriendlyName;
        }
        catch (Exception) { return null; }
    }

    private static Icon GetTrayIcon()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("AudioLeash.Resources.icon.ico");
            if (stream is not null)
                return new Icon(stream);
        }
        catch { /* fall through to default */ }
        return SystemIcons.Application;
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            WindowsTheme.Changed -= OnThemeChanged;
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);

            foreach (ToolStripItem item in _contextMenu.Items)
            {
                if (item is ToolStripMenuItem mi)
                {
                    if (mi.Tag is (MMDevice d, DataFlow _))
                        d.Dispose();
                    else if (mi.Tag is MMDevice d2)
                        d2.Dispose();
                }
            }

            _sectionHeaderFont.Dispose();
            _trayIcon.Dispose();
            _contextMenu.Dispose();
            _enumerator.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Receives Windows audio endpoint change notifications.
    /// Must be kept alive as a class field -- not a local variable -- to prevent
    /// the GC from collecting it while Windows holds a native COM pointer to it.
    /// (NAudio issue #849: ExecutionEngineException on GC collection)
    /// </summary>
    private sealed class AudioNotificationClient : IMMNotificationClient
    {
        private readonly Action<DataFlow, string> _onDefaultChanged;
        private readonly Action<string, DeviceState> _onDeviceStateChanged;

        internal AudioNotificationClient(
            Action<DataFlow, string> onDefaultChanged,
            Action<string, DeviceState> onDeviceStateChanged)
        {
            _onDefaultChanged = onDefaultChanged;
            _onDeviceStateChanged = onDeviceStateChanged;
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Windows can pass a null device ID when the last device of a role is removed.
            // NAudio declares the parameter as non-nullable, but the underlying COM call
            // can produce null. Guard against this to avoid spurious restore attempts.
            if (defaultDeviceId is null) return;

            // Only react to Multimedia role changes for both Render (playback) and
            // Capture (recording). Windows fires this for Console, Multimedia, and
            // Communications separately -- filtering to Multimedia prevents triple-firing.
            if (role == Role.Multimedia && (flow == DataFlow.Render || flow == DataFlow.Capture))
                _onDefaultChanged(flow, defaultDeviceId);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
            => _onDeviceStateChanged(deviceId, newState);

        // Required by IMMNotificationClient; AudioLeash does not act on these.
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
