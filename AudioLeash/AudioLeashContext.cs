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
    private readonly AudioNotificationClient _notificationClient;
    private readonly SettingsService         _settingsService;
    private readonly StartupService          _startupService;

    public AudioLeashContext()
    {
        _enumerator   = new MMDeviceEnumerator();
        _policyConfig = new PolicyConfigClient();
        _selection    = new DeviceSelectionState();
        _contextMenu  = new ContextMenuStrip();
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

        // Restore the previously selected device, or guide the user to pick one.
        string? savedId = _settingsService.LoadSelectedDeviceId();

        if (savedId is null)
        {
            UpdateTrayTooltip(null);
            if (!_settingsService.HasSettingsFile)
            {
                // Settings file does not exist yet — genuine first run.
                // Prompt the user to pick a device; the app is passive until they do.
                _trayIcon.ShowBalloonTip(
                    timeout:  4000,
                    tipTitle: "Welcome to AudioLeash",
                    tipText:  "Click the tray icon and select a device to enable auto-restore.",
                    tipIcon:  ToolTipIcon.Info);
            }
            // If the file exists but savedId is null, the user previously cleared their
            // selection or their saved device was not found on a prior run — stay passive silently.
        }
        else
        {
            var active = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            var savedDevice = active.FirstOrDefault(d => d.ID == savedId);
            bool available = savedDevice is not null;
            string? savedName = savedDevice?.FriendlyName;
            foreach (var d in active) d.Dispose();

            if (available)
            {
                _selection.SelectDevice(savedId);
                UpdateTrayTooltip(savedName);
            }
            else
            {
                // Saved device is not connected. Clear persisted selection and notify the user.
                _settingsService.SaveSelectedDeviceId(null);
                UpdateTrayTooltip(null);
                _trayIcon.ShowBalloonTip(
                    timeout:  4000,
                    tipTitle: "Saved Device Not Found",
                    tipText:  "Your saved audio device was not found. Select a device from the tray menu to re-enable auto-restore.",
                    tipIcon:  ToolTipIcon.Info);
            }
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
    /// Rebuilds the context menu with the current list of active playback devices.
    /// Called on every left-click and after any device switch.
    /// </summary>
    private void RefreshDeviceList()
    {
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

        // Seed the selection with whatever Windows currently uses as the default.
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

    private void DeviceMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: MMDevice device })
            return;

        try
        {
            _selection.IsInternalChange = true;

            string deviceId   = device.ID;
            string deviceName = device.FriendlyName;
            _policyConfig.SetDefaultEndpoint(deviceId);

            _selection.SelectDevice(deviceId);
            UpdateTrayTooltip(deviceName);
            _settingsService.SaveSelectedDeviceId(deviceId);

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
        UpdateTrayTooltip(null);
        _settingsService.SaveSelectedDeviceId(null);

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
    /// when the system default playback device changes.
    /// </summary>
    private void OnDefaultDeviceChanged(string newDefaultId)
    {
        bool isSelectedAvailable = false;
        if (_selection.SelectedDeviceId is not null)
        {
            var activeDevices = _enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .ToList();
            isSelectedAvailable = activeDevices.Any(d => d.ID == _selection.SelectedDeviceId);
            foreach (var d in activeDevices) d.Dispose();
        }

        var decision = _selection.EvaluateDefaultChange(newDefaultId, isSelectedAvailable);

        switch (decision)
        {
            case RestoreDecision.NoAction:
                return;

            case RestoreDecision.ClearSelection:
                _selection.ClearSelection();
                SafeInvoke(() =>
                {
                    _settingsService.SaveSelectedDeviceId(null);
                    UpdateTrayTooltip(null);
                    _trayIcon.ShowBalloonTip(
                        timeout:  3000,
                        tipTitle: "Audio Device Unavailable",
                        tipText:  "Your selected device is no longer available. Selection cleared.",
                        tipIcon:  ToolTipIcon.Warning);
                    RefreshDeviceList();
                });
                break;

            case RestoreDecision.Restore:
                // Set the flag on the audio thread BEFORE dispatching to the UI thread.
                // This prevents a second OnDefaultDeviceChanged from triggering another
                // restore attempt during the roundtrip to the UI thread.
                _selection.IsInternalChange = true;
                string restoreId = _selection.SelectedDeviceId!;

                // CPolicyConfigClient is an STA COM object with no registered proxy/stub.
                // Calling it from the audio COM thread (a different apartment) causes an
                // InvalidCastException. Marshal the call to the UI STA thread instead.
                // SafeInvoke uses Control.Invoke (synchronous), so the audio thread
                // blocks here until the UI thread finishes and resets the flag.
                // The outer try/finally ensures the flag is cleared even if SafeInvoke
                // itself throws (e.g. ObjectDisposedException during shutdown).
                try
                {
                    SafeInvoke(() =>
                    {
                        try
                        {
                            _policyConfig.SetDefaultEndpoint(restoreId);

                            var restoreDevices = _enumerator
                                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                .ToList();
                            string restoredName = restoreDevices.FirstOrDefault(d => d.ID == restoreId)?.FriendlyName ?? restoreId;
                            foreach (var d in restoreDevices) d.Dispose();

                            UpdateTrayTooltip(restoredName);

                            _trayIcon.ShowBalloonTip(
                                timeout:  3000,
                                tipTitle: "Audio Device Restored",
                                tipText:  $"Switched back to: {restoredName}",
                                tipIcon:  ToolTipIcon.Info);
                            RefreshDeviceList();
                        }
                        catch (Exception ex)
                        {
                            _trayIcon.ShowBalloonTip(
                                timeout:  3000,
                                tipTitle: "Restore Failed",
                                tipText:  $"Could not restore audio device:\n{ex.Message}",
                                tipIcon:  ToolTipIcon.Error);
                        }
                        finally
                        {
                            _selection.IsInternalChange = false;
                        }
                    });
                }
                catch
                {
                    _selection.IsInternalChange = false;
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

    private void UpdateTrayTooltip(string? deviceName)
    {
        string text = deviceName is null
            ? "AudioLeash — No device selected"
            : $"AudioLeash — {deviceName}";
        _trayIcon.Text = text.Length > 63 ? text[..62] + "…" : text;
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
                if (item is ToolStripMenuItem { Tag: MMDevice d })
                    d.Dispose();
            }

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
        private readonly Action<string> _onDefaultChanged;

        internal AudioNotificationClient(Action<string> onDefaultChanged)
            => _onDefaultChanged = onDefaultChanged;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Only react to playback default changing for Multimedia role.
            // Windows fires this for Console, Multimedia, and Communications separately --
            // filtering to Multimedia prevents triple-firing.
            if (flow == DataFlow.Render && role == Role.Multimedia)
                _onDefaultChanged(defaultDeviceId);
        }

        // Required by IMMNotificationClient; AudioLeash does not act on these.
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
