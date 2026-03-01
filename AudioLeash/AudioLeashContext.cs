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
        _notificationClient = new AudioNotificationClient(OnDefaultDeviceChanged);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);

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

        RefreshDeviceList();
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
        string? playbackName = GetDeviceName(_selection.SelectedDeviceId);
        string? captureName  = GetDeviceName(_captureSelection.SelectedDeviceId);

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

    private string? GetDeviceName(string? deviceId)
    {
        if (deviceId is null) return null;
        try
        {
            using var device = _enumerator.GetDevice(deviceId);
            return device.State == DeviceState.Active ? device.FriendlyName : null;
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

        internal AudioNotificationClient(Action<DataFlow, string> onDefaultChanged)
            => _onDefaultChanged = onDefaultChanged;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Only react to Multimedia role changes for both Render (playback) and
            // Capture (recording). Windows fires this for Console, Multimedia, and
            // Communications separately -- filtering to Multimedia prevents triple-firing.
            if (role == Role.Multimedia && (flow == DataFlow.Render || flow == DataFlow.Capture))
                _onDefaultChanged(flow, defaultDeviceId);
        }

        // Required by IMMNotificationClient; AudioLeash does not act on these.
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
