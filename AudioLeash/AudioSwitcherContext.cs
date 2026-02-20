using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Reactive.Linq;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace AudioLeash
{
    /// <summary>
    /// Application context that manages the system tray icon and audio device switching.
    /// Runs without a visible window — entirely through the notification area.
    /// </summary>
    public class AudioSwitcherContext : ApplicationContext
    {
        // ─── Fields ───────────────────────────────────────────────────────────────

        private NotifyIcon       trayIcon      = null!;
        private ContextMenuStrip contextMenu   = null!;
        private CoreAudioController audioController = null!;

        /// <summary>ID of the device the user explicitly selected via the menu.</summary>
        private Guid? selectedDeviceId;

        /// <summary>
        /// Prevents the AudioDeviceChanged handler from re-triggering when the app
        /// itself switches the default device.
        /// </summary>
        private bool isInternalChange;

        // ─── Constructor ──────────────────────────────────────────────────────────

        public AudioSwitcherContext()
        {
            InitializeAudioController();
            InitializeTrayIcon();

            // Seed selection with whatever Windows currently uses as default
            var currentDefault = audioController.DefaultPlaybackDevice;
            if (currentDefault != null)
                selectedDeviceId = currentDefault.Id;

            RefreshDeviceList();
        }

        // ─── Initialization ───────────────────────────────────────────────────────

        private void InitializeAudioController()
        {
            audioController = new CoreAudioController();

            // Subscribe to all audio device change events (Rx observable)
            audioController.AudioDeviceChanged.Subscribe(OnAudioDeviceChanged);
        }

        private void InitializeTrayIcon()
        {
            contextMenu = new ContextMenuStrip();

            trayIcon = new NotifyIcon
            {
                Icon             = GetTrayIcon(),
                ContextMenuStrip = contextMenu,
                Text             = "AudioLeash",
                Visible          = true
            };

            // Left-click opens the same context menu as right-click
            trayIcon.MouseClick += TrayIcon_MouseClick;
        }

        /// <summary>
        /// Returns a usable icon: tries to load a custom icon from the Resources
        /// folder, falls back to the standard application icon if not found.
        /// </summary>
        private static Icon GetTrayIcon()
        {
            try
            {
                string iconPath = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "Resources", "icon.ico");

                if (System.IO.File.Exists(iconPath))
                    return new Icon(iconPath);
            }
            catch { /* fall through */ }

            return SystemIcons.Application;
        }

        // ─── Tray Icon Events ─────────────────────────────────────────────────────

        private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Rebuild list so newly connected/disconnected devices appear
                RefreshDeviceList();

                // Use reflection to trigger the context menu on left-click
                // (WinForms only shows it automatically on right-click)
                MethodInfo? showMenu = typeof(NotifyIcon).GetMethod(
                    "ShowContextMenu",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                showMenu?.Invoke(trayIcon, null);
            }
        }

        // ─── Device List ──────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the context menu with the current list of active playback devices.
        /// Called on every left-click and after any device switch.
        /// </summary>
        private void RefreshDeviceList()
        {
            contextMenu.Items.Clear();

            var playbackDevices = audioController.GetPlaybackDevices()
                .Where(d => d.State == DeviceState.Active)
                .OrderBy(d => d.FullName)
                .ToList();

            var defaultDevice = audioController.DefaultPlaybackDevice;

            if (playbackDevices.Count == 0)
            {
                contextMenu.Items.Add(new ToolStripMenuItem("No devices available") { Enabled = false });
            }
            else
            {
                foreach (var device in playbackDevices)
                {
                    bool isUserSelected  = selectedDeviceId.HasValue && selectedDeviceId.Value == device.Id;
                    bool isWindowsDefault = defaultDevice?.Id == device.Id;

                    // Build display name:
                    //   ✔ = user-selected  |  (Windows default) = currently active in OS but not user-selected
                    string label = device.FullName;
                    if (isWindowsDefault && !isUserSelected)
                        label += "  (Windows default)";

                    var menuItem = new ToolStripMenuItem(label)
                    {
                        Tag     = device,
                        Checked = isUserSelected   // checkmark tracks user selection
                    };

                    menuItem.Click += DeviceMenuItem_Click;
                    contextMenu.Items.Add(menuItem);
                }
            }

            contextMenu.Items.Add(new ToolStripSeparator());

            // "Clear Selection" disables auto-restore and lets Windows manage the default freely
            var clearItem = new ToolStripMenuItem("Clear Selection  (disable auto-restore)")
            {
                Enabled = selectedDeviceId.HasValue
            };
            clearItem.Click += ClearSelection_Click;
            contextMenu.Items.Add(clearItem);

            var refreshItem = new ToolStripMenuItem("Refresh List");
            refreshItem.Click += (_, _) => RefreshDeviceList();
            contextMenu.Items.Add(refreshItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += Exit_Click;
            contextMenu.Items.Add(exitItem);
        }

        // ─── Device Selection ─────────────────────────────────────────────────────

        private async void DeviceMenuItem_Click(object? sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem { Tag: CoreAudioDevice device })
                return;

            try
            {
                isInternalChange = true;
                await device.SetAsDefaultAsync();
                selectedDeviceId = device.Id;

                trayIcon.ShowBalloonTip(
                    timeout: 2500,
                    tipTitle: "Audio Device Selected",
                    tipText: $"Default output: {device.FullName}\n\nAuto-restore is now active.",
                    tipIcon: ToolTipIcon.Info);

                RefreshDeviceList();
            }
            catch (Exception ex)
            {
                ShowError($"Could not set device:\n{ex.Message}");
            }
            finally
            {
                isInternalChange = false;
            }
        }

        private void ClearSelection_Click(object? sender, EventArgs e)
        {
            selectedDeviceId = null;

            trayIcon.ShowBalloonTip(
                timeout: 2500,
                tipTitle: "Selection Cleared",
                tipText: "Auto-restore disabled. Select a device to re-enable.",
                tipIcon: ToolTipIcon.Info);

            RefreshDeviceList();
        }

        // ─── Device Change Monitoring ─────────────────────────────────────────────

        /// <summary>
        /// Fired by the AudioSwitcher Rx observable whenever any audio device event occurs.
        /// Restores the user-selected device if Windows changes the default externally.
        /// </summary>
        private async void OnAudioDeviceChanged(DeviceChangedArgs args)
        {
            // We only care when Windows changes which device is the default
            if (args.ChangedType != DeviceChangedType.DefaultChanged)
                return;

            // Ignore events we triggered ourselves to avoid feedback loops
            if (isInternalChange)
                return;

            // Nothing to restore if no device has been explicitly selected
            if (!selectedDeviceId.HasValue)
                return;

            var newDefault = audioController.DefaultPlaybackDevice;

            // Already on the right device — nothing to do
            if (newDefault?.Id == selectedDeviceId.Value)
                return;

            // Check whether the selected device is still present and active
            var selectedDevice = audioController.GetPlaybackDevices()
                .FirstOrDefault(d => d.Id == selectedDeviceId.Value
                                  && d.State == DeviceState.Active);

            if (selectedDevice == null)
            {
                // Device disappeared (unplugged etc.) — clear selection gracefully
                selectedDeviceId = null;

                SafeInvoke(() =>
                {
                    trayIcon.ShowBalloonTip(
                        timeout: 3000,
                        tipTitle: "Audio Device Unavailable",
                        tipText: "Your selected device is no longer available. Selection cleared.",
                        tipIcon: ToolTipIcon.Warning);

                    RefreshDeviceList();
                });

                return;
            }

            // Restore the user's chosen device
            try
            {
                isInternalChange = true;
                await selectedDevice.SetAsDefaultAsync();

                SafeInvoke(() =>
                {
                    trayIcon.ShowBalloonTip(
                        timeout: 3000,
                        tipTitle: "Audio Device Restored",
                        tipText: $"Switched back to: {selectedDevice.FullName}",
                        tipIcon: ToolTipIcon.Info);

                    RefreshDeviceList();
                });
            }
            catch (Exception ex)
            {
                SafeInvoke(() =>
                {
                    trayIcon.ShowBalloonTip(
                        timeout: 3000,
                        tipTitle: "Restore Failed",
                        tipText: $"Could not restore audio device:\n{ex.Message}",
                        tipIcon: ToolTipIcon.Error);
                });
            }
            finally
            {
                isInternalChange = false;
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Marshals an action back to the UI thread if required.
        /// The AudioDeviceChanged event may arrive on a background/Rx thread.
        /// </summary>
        private void SafeInvoke(Action action)
        {
            if (contextMenu.InvokeRequired)
                contextMenu.Invoke(action);
            else
                action();
        }

        private static void ShowError(string message) =>
            MessageBox.Show(message, "AudioLeash",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        // ─── Exit ─────────────────────────────────────────────────────────────────

        private void Exit_Click(object? sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trayIcon?.Dispose();
                contextMenu?.Dispose();
                audioController?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
