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
    private readonly NotifyIcon           _trayIcon;
    private readonly ContextMenuStrip     _contextMenu;
    private readonly MMDeviceEnumerator   _enumerator;
    private readonly PolicyConfigClient   _policyConfig;
    private readonly DeviceSelectionState _selection;
    private readonly AudioNotificationClient _notificationClient;

    public AudioLeashContext()
    {
        _enumerator   = new MMDeviceEnumerator();
        _policyConfig = new PolicyConfigClient();
        _selection    = new DeviceSelectionState();
        _contextMenu  = new ContextMenuStrip();

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
        // If the GC collects it while Windows holds a native COM pointer the process
        // will crash with ExecutionEngineException. (NAudio issue #849)
        _notificationClient = new AudioNotificationClient(OnDefaultDeviceChanged);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);

        // Seed the selection with whatever Windows currently uses as the default.
        using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (defaultDevice is not null)
            _selection.SelectDevice(defaultDevice.ID);

        RefreshDeviceList();
    }

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

        var refreshItem = new ToolStripMenuItem("Refresh List");
        refreshItem.Click += (_, _) => RefreshDeviceList();
        _contextMenu.Items.Add(refreshItem);

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
                break;
        }
    }

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
